using System.Diagnostics;
using System.Globalization;
using System.Text;
namespace PersistentBridge;

/// <summary>
/// A persistent bash or zsh process. Variables, functions, exported environment and the working directory
/// survive between approved commands because every command is sourced into the same shell.
///
/// Protocol: the approved command text is written verbatim to a private 0600 file and sourced with stdin
/// redirected from /dev/null. After it finishes the shell prints a random per-request marker on both streams;
/// stdout also carries the exit status. The working directory is read from /proc, never from the shell.
/// The shell runs in its own process group, so a timeout, cancellation or Stop kills it with its children.
///
/// TRUST BOUNDARY: the shell runs as the operator, in the same user account as this bridge. An approved
/// command can therefore also reach this channel (for example through /proc/&lt;pid&gt;/fd) and a process it
/// leaves running can interfere with later requests. Approval authorizes everything a command can do,
/// including leaving processes behind. After every request the group is swept and any survivors are
/// reported in the result; Reset session ends them. A daemonizing process that calls setsid leaves the group
/// and is not tracked.
/// </summary>
internal sealed class ShellSession(ShellProfile profile, bool elevated = false) : ISessionBackend
{
    private const int OutputLimit = 65536;
    private const int StatusLimit = 64;
    private const int DirectoryLimit = 4096;
    private static readonly TimeSpan StartupLimit = TimeSpan.FromSeconds(10);
    private static readonly UTF8Encoding Utf8 = new(false);
    private static readonly string[] BlockedEnvironment = ["CONTROL_PLANE_API_KEY", "BASH_ENV", "ENV", "SHELLOPTS", "BASHOPTS", "PS4"];
    private readonly object sync = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private Process? child;
    private volatile bool healthy = true;
    private volatile string directory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public Guid Id { get; } = Guid.NewGuid();
    public string Directory => directory;
    public bool Healthy => healthy;

    private sealed record Marker(string Status, string Leftover);

    public async Task<Execution> Run(string command, int seconds, CancellationToken stop)
    {
        stop.ThrowIfCancellationRequested();
        if (elevated || !healthy) throw new InvalidOperationException("Session unavailable; local elevation is unsupported.");
        if (command == profile.PinnedRead) return await RunPinnedRead(seconds, stop);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop);
        deadline.CancelAfter(TimeSpan.FromSeconds(seconds));
        var output = new CappedText(); var errors = new CappedText();
        string? scriptFile = null; bool held = false;
        try {
            // One request at a time: wrapper lines and the stream readers must never interleave.
            await gate.WaitAsync(deadline.Token); held = true;
            var shell = await EnsureShell(deadline.Token);
            var nonce = Guid.NewGuid().ToString("N");
            scriptFile = WriteScript(command);
            var outRead = ReadUntilMarker(shell.StandardOutput, nonce, output, deadline.Token);
            var errRead = ReadUntilMarker(shell.StandardError, nonce, errors, deadline.Token);
            await shell.StandardInput.WriteLineAsync(Wrapper(scriptFile, nonce).AsMemory(), deadline.Token);
            await shell.StandardInput.FlushAsync(deadline.Token);
            await Task.WhenAll(outRead, errRead);
            var status = await outRead; var errStatus = await errRead;
            // EOF on either stream means the shell exited or a command closed a standard stream (exec 2>&-).
            if (status == null || errStatus == null) return ShellExited(shell, output, errors, streamClosed: status != null);
            if (!int.TryParse(status.Status, NumberStyles.None, CultureInfo.InvariantCulture, out int code))
                throw new IOException("Malformed status line.");
            RefreshDirectory(shell.Id);
            string notes = Survivors(shell.Id) + (status.Leftover.Length + errStatus.Leftover.Length > 0
                ? "Output that arrived after the command finished (background job) was discarded.\n" : "");
            return new(code == 0 ? "completed" : "failed", output.Text, errors.Text + notes, code, directory,
                Healthy: healthy, Truncated: output.Truncated || errors.Truncated);
        } catch (OperationCanceledException) {
            Dispose();
            return new(stop.IsCancellationRequested ? "cancelled" : "timed_out", output.Text,
                "Shell stopped. Session state lost; side effects may remain.", Directory: directory, Healthy: false, Truncated: output.Truncated);
        } catch {
            Dispose();
            return new("session_unhealthy", output.Text, "Shell disconnected. Execution outcome unknown.", Directory: directory, Healthy: false);
        } finally {
            if (scriptFile != null) try { File.Delete(scriptFile); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            if (held) gate.Release();
        }
    }

    private Execution ShellExited(Process shell, CappedText output, CappedText errors, bool streamClosed)
    {
        int? code = null;
        try { if (shell.WaitForExit(500)) code = shell.ExitCode; } catch (InvalidOperationException) { }
        Dispose();
        string reason = streamClosed
            ? "A command closed stdout or stderr (for example exec 2>&-). Session ended because results can no longer be read; use Reset session."
            : "The shell exited (for example via exit, exec or set -e). Session state lost; use Reset session.";
        return new(code is 0 ? "completed" : "failed", output.Text,
            errors.Text + (errors.Text.Length > 0 && !errors.Text.EndsWith('\n') ? "\n" : "") + reason,
            code, directory, Healthy: false, Truncated: output.Truncated || errors.Truncated);
    }

    private async Task<Process> EnsureShell(CancellationToken token)
    {
        Process shell; bool fresh;
        lock (sync) {
            if (!healthy) throw new InvalidOperationException("Session lost. Reset locally.");
            fresh = child == null;
            if (fresh) child = Launch();
            shell = child!;
        }
        if (fresh) await Handshake(shell, token);
        return shell;
    }

    private Process Launch()
    {
        string setsid = ShellProfile.FindExecutable("setsid") ?? throw new IOException("setsid (util-linux) is required to isolate the shell in its own process group.");
        string exe = ShellProfile.FindExecutable(profile.Kind == ShellKind.Zsh ? "zsh" : "bash") ?? throw new IOException($"{profile.DisplayName} not found.");
        var start = new ProcessStartInfo(setsid) {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = directory, StandardInputEncoding = Utf8, StandardOutputEncoding = Utf8, StandardErrorEncoding = Utf8 };
        start.ArgumentList.Add(exe);
        // No profile or rc files (zsh still reads the root-owned /etc/zshenv).
        if (profile.Kind == ShellKind.Zsh) start.ArgumentList.Add("-f"); else { start.ArgumentList.Add("--noprofile"); start.ArgumentList.Add("--norc"); }
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("PSBRIDGE_") || k.StartsWith("BASH_FUNC_") || BlockedEnvironment.Contains(k)).ToArray())
            start.Environment.Remove(key);
        return Process.Start(start) ?? throw new IOException("Shell failed to start.");
    }

    private static async Task Handshake(Process shell, CancellationToken token)
    {
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(token);
        startup.CancelAfter(StartupLimit);
        string ready = Guid.NewGuid().ToString("N");
        await shell.StandardInput.WriteLineAsync($"builtin printf '%s\\n' {ready}".AsMemory(), startup.Token);
        await shell.StandardInput.FlushAsync(startup.Token);
        if (await shell.StandardOutput.ReadLineAsync(startup.Token) != ready) throw new IOException("Shell did not become ready.");
        // Fail closed unless the shell leads its own process group; only then is group-kill on timeout safe and complete.
        if (ProcessGroupOf(shell.Id) != shell.Id) throw new IOException("Shell is not isolated in its own process group.");
    }

    /// <summary>Process group id from /proc/&lt;pid&gt;/stat, or -1 if the process is gone or is a zombie.</summary>
    private static int ProcessGroupOf(int pid)
    {
        try {
            var stat = File.ReadAllText($"/proc/{pid}/stat");
            var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' '); // state ppid pgrp ...
            return fields[0] == "Z" ? -1 : int.Parse(fields[2], CultureInfo.InvariantCulture);
        } catch (Exception e) when (e is IOException or FormatException or ArgumentOutOfRangeException or UnauthorizedAccessException) { return -1; }
    }

    private static string Survivors(int group)
    {
        var pids = new List<int>();
        foreach (var dir in System.IO.Directory.EnumerateDirectories("/proc")) {
            if (!int.TryParse(Path.GetFileName(dir), NumberStyles.None, CultureInfo.InvariantCulture, out int pid) || pid == group) continue;
            if (ProcessGroupOf(pid) == group) pids.Add(pid);
        }
        return pids.Count == 0 ? "" :
            $"Note: {pids.Count} background process(es) are still running in this session (pid {string.Join(", ", pids.Take(8))}). " +
            "They run as you and can interfere with later requests. Reset session ends them.\n";
    }

    // The shell's own $PWD can be assigned by a command and shown to the operator in the next approval, so the
    // directory is read from the kernel instead.
    private void RefreshDirectory(int pid)
    {
        try {
            var target = new FileInfo($"/proc/{pid}/cwd").ResolveLinkTarget(false)?.FullName;
            if (target is { Length: > 0 and <= DirectoryLimit } && target[0] == '/' && !target.Any(char.IsControl)) directory = target;
        } catch (IOException) { }
    }

    private static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";

    // One line so a stuck or half-quoted command cannot swallow it. `builtin` skips functions named printf or `.`,
    // but a function named builtin or an alias planted by an earlier approved command can still intercept it.
    private static string Wrapper(string file, string nonce) =>
        $"builtin . {Quote(file)} </dev/null; __psbridge_rc=$?; " +
        $"builtin printf '\\n%s %d\\n' {Quote(nonce)} \"$__psbridge_rc\"; " +
        $"builtin printf '\\n%s\\n' {Quote(nonce)} >&2; unset __psbridge_rc";

    private static string WriteScript(string command)
    {
        var dir = Path.Combine(LinuxOS.PrivateRoot().FullName, "shell-commands");
        System.IO.Directory.CreateDirectory(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".sh");
        using var file = new FileStream(path, new FileStreamOptions {
            Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite });
        file.Write(Utf8.GetBytes(command + "\n"));
        return path;
    }

    /// <summary>
    /// Copies text to <paramref name="sink"/> until the request marker and returns the rest of the marker line
    /// (the exit status on stdout, empty on stderr) plus any bytes that followed it, or null if the stream ended
    /// first. The marker line is always consumed so the next request starts on a clean stream.
    /// </summary>
    private static async Task<Marker?> ReadUntilMarker(StreamReader reader, string nonce, CappedText sink, CancellationToken token)
    {
        string marker = "\n" + nonce, carry = "";
        var buffer = new char[4096];
        while (true) {
            int n = await reader.ReadAsync(buffer.AsMemory(), token);
            if (n == 0) { sink.Add(carry); return null; }
            string text = carry + new string(buffer, 0, n);
            int at = text.IndexOf(marker, StringComparison.Ordinal);
            if (at >= 0) {
                sink.Add(text[..at]);
                string rest = text[(at + marker.Length)..];
                while (!rest.Contains('\n')) {
                    if (rest.Length > StatusLimit) throw new IOException("Status line too long.");
                    n = await reader.ReadAsync(buffer.AsMemory(), token);
                    if (n == 0) break;
                    rest += new string(buffer, 0, n);
                }
                int eol = rest.IndexOf('\n');
                if (eol < 0 || eol > StatusLimit) throw new IOException("Status line malformed.");
                return new Marker(rest[..eol].Trim(), rest[(eol + 1)..]);
            }
            int keep = Math.Min(text.Length, marker.Length - 1);
            sink.Add(text[..^keep]);
            carry = text[^keep..];
        }
    }

    private async Task<Execution> RunPinnedRead(int seconds, CancellationToken stop)
    {
        // A fresh process, never the persistent shell: earlier commands cannot shadow `date` with a function or alias.
        string date = ShellProfile.FindExecutable("date") ?? throw new IOException("date not found.");
        var start = new ProcessStartInfo(date) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--iso-8601=seconds");
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("PSBRIDGE_") || BlockedEnvironment.Contains(k)).ToArray())
            start.Environment.Remove(key);
        using var process = Process.Start(start) ?? throw new IOException("date failed to start.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop);
        deadline.CancelAfter(TimeSpan.FromSeconds(seconds));
        try {
            var text = process.StandardOutput.ReadToEndAsync(deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
            return new(process.ExitCode == 0 ? "completed" : "failed", (await text).Trim(), Directory: directory);
        } catch (OperationCanceledException) {
            try { process.Kill(true); } catch (InvalidOperationException) { }
            return new(stop.IsCancellationRequested ? "cancelled" : "timed_out", Error: "date did not finish.", Directory: directory);
        }
    }

    public void Dispose()
    {
        lock (sync) {
            healthy = false;
            if (child == null) return;
            try {
                LinuxOS.kill(-child.Id, 9);
                if (!child.HasExited) child.Kill(true);
                child.WaitForExit(2000); // reap, so no zombie outlives the session
            } catch (InvalidOperationException) { }
            finally { child.Dispose(); child = null; }
        }
    }

    private sealed class CappedText
    {
        private readonly StringBuilder text = new();
        public bool Truncated { get; private set; }
        public string Text => text.ToString();
        public void Add(string value)
        {
            int room = OutputLimit - text.Length;
            int take = Math.Max(0, Math.Min(value.Length, room));
            if (take < value.Length) {
                Truncated = true;
                // Never cut a surrogate pair in half: a lone surrogate cannot be serialized to JSON.
                if (take > 0 && char.IsHighSurrogate(value[take - 1])) take--;
            }
            if (take > 0) text.Append(value.AsSpan(0, take));
        }
    }
}
