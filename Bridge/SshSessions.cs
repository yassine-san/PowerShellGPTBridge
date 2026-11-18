using Renci.SshNet;
using System.Text;
using System.Text.Json;
namespace PersistentBridge;
internal sealed record Host(string Address, int Port, string FriendlyName, string OperatingSystem, string Fingerprint, string Username, string Authentication);
internal sealed class SshSessions(IOperatorView window, Audit audit) : IDisposable
{
    private readonly Dictionary<string, SshClient> clients = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PrivateKeyFile> keys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> secrets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Guid> ids = new(StringComparer.OrdinalIgnoreCase);
    private string Registry => Path.Combine(CredentialStore.Root, "hosts.json");
    private Dictionary<string, Host> Hosts => File.Exists(Registry) ? JsonSerializer.Deserialize<Dictionary<string, Host>>(File.ReadAllText(Registry))! : new();
    public string Key(string host, int port) => host.ToLowerInvariant() + ":" + port;
    public bool Connected(string key) => clients.TryGetValue(key, out var client) && client.IsConnected;
    public string Identity(string key) => clients.TryGetValue(key, out var client) ? client.ConnectionInfo.Username : "not authenticated";
    public Guid Id(string key) => ids.GetValueOrDefault(key);
    public async Task<Credential?> Credential(string key, CancellationToken token) => CredentialStore.Load(key) ?? await window.RequestCredential(key, false, token);
    public async Task Connect(string host, int port, Credential credential, Guid requestId, Guid taskId, System.Diagnostics.Stopwatch budget, int seconds, CancellationToken token)
    {
        var key = Key(host, port); if (Connected(key)) throw new InvalidOperationException("Already connected");
        var hosts = Hosts; var pinned = hosts.GetValueOrDefault(key); string? accepted = null;
        PrivateKeyFile? privateKey = credential.Method == "private-key" ? new PrivateKeyFile(credential.KeyPath, credential.Secret) : null;
        AuthenticationMethod auth = privateKey != null ? new PrivateKeyAuthenticationMethod(credential.Username, privateKey) : new PasswordAuthenticationMethod(credential.Username, credential.Secret);
        using var handshake = CancellationTokenSource.CreateLinkedTokenSource(token);
        var networkLimit = TimeSpan.FromSeconds(Math.Min(15,seconds));
        var networkClock = System.Diagnostics.Stopwatch.StartNew();
        handshake.CancelAfter(networkLimit);
        var info = new ConnectionInfo(host, port, credential.Username, auth) { Timeout = TimeSpan.FromSeconds(pinned == null ? 80 : 15) };
        var client = new SshClient(info); bool keyRejected = false; HostApprovalException? hostDecision = null; bool shortBudget = false;
        client.HostKeyReceived += (_, e) => {
            e.CanTrust = false;
            e.CanTrust = HostKeyPin.Accept(pinned?.Fingerprint,e.HostKey,ReviewNew,
                (fingerprint,matches) => {
                    keyRejected = !matches;
                    audit.Write("host_key_check", new { id = requestId, target = key, fingerprint, accepted = matches });
                }, handshake.Token);
            bool ReviewNew(string fingerprint)
            {
            if (token.IsCancellationRequested || budget.Elapsed.TotalSeconds >= 44.5) { shortBudget = true; return false; }
            handshake.CancelAfter(Timeout.InfiniteTimeSpan);
            networkClock.Stop();
            handshake.Token.ThrowIfCancellationRequested();
            var hostApprovalId = Guid.NewGuid();
            var op = new Operation(hostApprovalId, key, "SSH host authorization", credential.Username, "REMOTE PRIVILEGES UNKNOWN",
                $"Authorize host {key}\nHost key: {e.HostKeyName}\nFingerprint: {fingerprint}", "n/a", 15,
                "Authorize a new SSH host", "Store its fingerprint locally", "Verify this fingerprint independently. Accepting an attacker key exposes authentication and data.", TaskId: taskId);
            var entry = new HistoryEntry(op, taskId, Guid.Empty, DateTimeOffset.UtcNow, "awaiting_approval");
            window.Record(entry);
            using var approval = CancellationTokenSource.CreateLinkedTokenSource(handshake.Token);
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            approval.CancelAfter(TimeSpan.FromSeconds(60));
            var decision = window.Approve(op, approval.Token, start).GetAwaiter().GetResult();
            bool consent = decision.Approved && !approval.IsCancellationRequested && System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalSeconds < 60;
            window.Record(entry with { Status = consent ? "approved" : decision.Status, Approval = consent ? "approved" : decision.Status,
                Decided = DateTimeOffset.UtcNow, Ended = DateTimeOffset.UtcNow, Reason = decision.Reason });
            audit.Write("host_authorization", new { id = hostApprovalId, parent_id = requestId, target = key, fingerprint, approved = consent });
            var left = networkLimit - networkClock.Elapsed;
            if (left <= TimeSpan.Zero) { handshake.Cancel(); return false; }
            networkClock.Start(); handshake.CancelAfter(left);
            consent = consent && !approval.IsCancellationRequested && System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalSeconds < 60;
            if (consent) accepted = fingerprint;
            else hostDecision = new(hostApprovalId, decision with { Approved = false, Status = token.IsCancellationRequested ? "cancelled" : decision.Approved ? "approval_expired" : decision.Status });
            return consent;
            }
        };
        try {
            await client.ConnectAsync(handshake.Token);
            if (!client.IsConnected) throw new IOException("SSH not connected");
            if (accepted != null) { hosts[key] = new Host(host, port, host, "unknown", accepted, credential.Username, credential.Method);
                File.WriteAllText(Registry + ".tmp", JsonSerializer.Serialize(hosts, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(Registry + ".tmp",Registry,true); }
            CredentialStore.Save(key, credential); clients[key] = client; if (privateKey != null) keys[key] = privateKey; secrets[key] = credential.Secret; ids[key] = Guid.NewGuid();
        } catch { client.Dispose(); privateKey?.Dispose(); CredentialStore.Forget(key);
            if (handshake.IsCancellationRequested && !token.IsCancellationRequested) throw new TimeoutException("SSH handshake timed out.");
            if (shortBudget) throw new TimeoutException("Insufficient local request budget for a new host-key approval. SSH authentication did not complete.");
            if (hostDecision != null) throw hostDecision;
            if (keyRejected) throw new InvalidOperationException("Host key rejected or changed. Verify independently; no command executed."); throw; }
    }
    public async Task<Execution> Run(string key, string command, int seconds, CancellationToken token, string? sudoPassword = null)
    {
        if (!Connected(key)) return new("connection_failed", Error: "Connect explicitly. No automatic reconnect/retry.", Healthy: false);
        var client = clients[key]; using var shell = client.CreateCommand(command); shell.CommandTimeout = TimeSpan.FromSeconds(seconds);
        var stdout = new BoundedText(); var stderr = new BoundedText();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token); cancel.CancelAfter(TimeSpan.FromSeconds(seconds));
        try {
            var execution = shell.ExecuteAsync(cancel.Token);
            // Drain both streams concurrently; never use Result/Error (which accumulate entire output).
            var outTask = Drain(shell.OutputStream, stdout, cancel.Token); var errTask = Drain(shell.ExtendedOutputStream, stderr, cancel.Token);
            if (sudoPassword != null) { using var input = shell.CreateInputStream(); var bytes = Encoding.UTF8.GetBytes(sudoPassword + "\n");
                try { await input.WriteAsync(bytes, cancel.Token); } finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); } }
            await Task.WhenAll(execution, outTask, errTask).WaitAsync(cancel.Token);
            return new(shell.ExitStatus == 0 ? "completed" : "failed", Redact(key, stdout.Text, sudoPassword), Redact(key, stderr.Text, sudoPassword), shell.ExitStatus, "SSH exec channel (no persistent cwd)", client.IsConnected, stdout.Truncated || stderr.Truncated);
        } catch (OperationCanceledException) { var partial = Redact(key, stdout.Text, sudoPassword); Disconnect(key); return new(token.IsCancellationRequested ? "cancelled" : "timed_out", partial,
            "SSH channel closed; remote process termination is NOT guaranteed.", Healthy: false, Truncated: stdout.Truncated || stderr.Truncated); }
        catch { var partial = Redact(key, stdout.Text, sudoPassword); Disconnect(key); return new("connection_failed", partial, "SSH operation failed. No automatic retry; remote effects may have occurred.", Healthy: false); }
    }
    private static async Task Drain(Stream stream, BoundedText text, CancellationToken token) { using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen:true);
        char[] buffer = new char[4096]; int count; while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) > 0) text.Add(new string(buffer, 0, count), false); }
    private string Redact(string key, string text, string? extra = null) {
        if (secrets.TryGetValue(key, out var secret) && !string.IsNullOrEmpty(secret)) text = text.Replace(secret, "[REDACTED]");
        return string.IsNullOrEmpty(extra) ? text : text.Replace(extra, "[REDACTED]");
    }
    public async Task<Execution> Transfer(string key, string action, string local, string remote, int seconds, CancellationToken token, bool captureDiff = false) {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token); stop.CancelAfter(TimeSpan.FromSeconds(seconds));
        using var sftp = new SftpClient(clients[key].ConnectionInfo); sftp.OperationTimeout = TimeSpan.FromSeconds(seconds);
        var expected = Hosts[key].Fingerprint;
        sftp.HostKeyReceived += (_, e) => {
            e.CanTrust = false;
            e.CanTrust = HostKeyPin.Matches(expected,e.HostKey);
        };
        // Disposal cancels a blocking transfer; a partial file may remain. Never overwrite or auto-retry.
        using var registration = stop.Token.Register(() => { try { sftp.Dispose(); } catch { } });
        string? fileDiff = null;
        try { await Task.Run(() => {
            sftp.Connect(); stop.Token.ThrowIfCancellationRequested();
            if (action == "upload") {
                using var file = new FileStream(local, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (file.Length > 16 * 1024 * 1024) throw new InvalidOperationException("Transfer limit is 16 MiB");
                sftp.UploadFile(file, remote, canOverride:false);
            } else {
                using var file = new FileStream(local, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var bounded = new LimitedWrite(file, 16 * 1024 * 1024, captureDiff);
                sftp.DownloadFile(remote, bounded);
                fileDiff = bounded.NewFileDiff();
            }
        }, stop.Token).WaitAsync(stop.Token);
        return new("completed", "Transfer completed (no overwrite).", Directory: "n/a", FileDiff: fileDiff);
        } catch (OperationCanceledException) { return new(token.IsCancellationRequested ? "cancelled" : "timed_out", Error:"Transfer stopped; partial destination may remain."); }
        catch { return new("failed", Error:"Transfer failed; partial destination may remain. No automatic retry."); }
    }
    public void Disconnect(string key) { if (clients.Remove(key, out var client)) client.Dispose(); ids.Remove(key); if (keys.Remove(key, out var pk)) pk.Dispose(); secrets.Remove(key); }
    public void Dispose() { foreach(var key in clients.Keys.ToArray()) Disconnect(key); }
    public static string Sudo(string command) => "sudo -k -S -p '' -- /bin/sh -c '" + command.Replace("'", "'\"'\"'") + "'";
}
