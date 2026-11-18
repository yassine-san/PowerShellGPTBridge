using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
namespace PersistentBridge;
internal sealed class Session : IDisposable
{
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint pid);
    private NamedPipeServerStream? pipe; private Process? child; private Job? job;
    public Guid Id { get; } = Guid.NewGuid();
    public string Directory { get; private set; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public string Identity { get; private set; } = IdentityInfo.Name;
    public string Privilege { get; private set; } = IdentityInfo.Privilege;
    public bool Healthy { get; private set; } = true;
    private readonly bool elevated;
    public Session(bool elevated = false) { this.elevated = elevated; }
    public async Task Start(CancellationToken token) {
        token.ThrowIfCancellationRequested();
        if (!Healthy) throw new InvalidOperationException("Session lost. Reset locally.");
        if (pipe != null) return;
        string name = "psbridge-" + Guid.NewGuid().ToString("N");
        pipe = new(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = elevated, CreateNoWindow = !elevated, RedirectStandardInput = !elevated, RedirectStandardOutput = !elevated, RedirectStandardError = !elevated };
        start.ArgumentList.Add(elevated ? "--broker" : "--worker"); start.ArgumentList.Add(name); start.ArgumentList.Add(Environment.ProcessId.ToString());
        if (elevated) start.Verb = "runas";
        else { foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("PSBRIDGE_") || k == "CONTROL_PLANE_API_KEY").ToArray()) start.Environment.Remove(key); }
        try {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(20));
            // ShellExecute/runas can wait on OS consent. Never hold the request indefinitely.
            var launching = Task.Run(() => Process.Start(start));
            try { child = await launching.WaitAsync(deadline.Token) ?? throw new IOException("Worker did not start"); }
            catch {
                _ = launching.ContinueWith(t => { if (t.Status == TaskStatus.RanToCompletion && t.Result != null) {
                    try { if (!elevated && !t.Result.HasExited) t.Result.Kill(true); } catch { }
                    t.Result.Dispose(); // A late elevated broker finds a closed pipe and cannot receive a command.
                } }, TaskScheduler.Default);
                throw;
            }
            if (!elevated) { child.StandardInput.Close(); job = new Job(); job.Add(child); _ = Drain(child.StandardOutput); _ = Drain(child.StandardError); }
            await pipe.WaitForConnectionAsync(deadline.Token);
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var pid) || pid != child.Id) throw new IOException("Wrong IPC client");
            var hello = await Wire.Read<WorkerHello>(pipe, deadline.Token);
            if (hello.Identity != IdentityInfo.Name || hello.Privilege != (elevated ? "ELEVATED — ADMINISTRATOR" : IdentityInfo.Privilege)) throw new IOException("Worker identity/privilege changed");
            Identity = hello.Identity; Privilege = hello.Privilege; Directory = hello.Directory;
        } catch { Dispose(); throw; }
    }
    public async Task<Execution> Run(string command, int seconds, CancellationToken stop) {
        await Start(stop); using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop); timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
        try {
            await Wire.Send(pipe!, new WorkerRequest(command, seconds), timeout.Token);
            var result = await Wire.Read<Execution>(pipe!, timeout.Token); Directory = result.Directory;
            if (!result.Healthy) Dispose(); return result;
        } catch (OperationCanceledException) { Dispose(); return new(stop.IsCancellationRequested ? "cancelled" : "timed_out", Error: "Worker stopped; session state lost. No automatic retry.", Directory: Directory, Healthy: false); }
        catch { Dispose(); return new("session_unhealthy", Error: "Worker disconnected. Outcome may be incomplete; do not retry blindly.", Directory: Directory, Healthy: false); }
    }
    private static async Task Drain(StreamReader reader) { try { char[] buffer = new char[4096]; while (await reader.ReadAsync(buffer) != 0) { } } catch { } }
    public void Dispose() { Healthy = false; pipe?.Dispose(); job?.Dispose();
        try { if (child != null && !child.HasExited && !elevated) child.Kill(true); } catch { }
        child?.Dispose(); }
}
