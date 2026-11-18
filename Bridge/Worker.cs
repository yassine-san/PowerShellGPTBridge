using System.Diagnostics;
using System.IO.Pipes;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
namespace PersistentBridge;
internal static class Worker
{
    public static int Entry(string[] args)
    {
        try { return Run(args).GetAwaiter().GetResult(); }
        catch { return 1; }
    }
    private static async Task<int> Run(string[] args)
    {
        if (args.Length != 3) return 1;
        using var parent = Process.GetProcessById(int.Parse(args[2]));
        if (!string.Equals(parent.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) return 1;
        _ = Task.Run(async () => { await parent.WaitForExitAsync(); Environment.Exit(1); });
        using var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var connect = new CancellationTokenSource(TimeSpan.FromSeconds(20)); await pipe.ConnectAsync(connect.Token);
        await Wire.Send(pipe, new WorkerHello(IdentityInfo.Name, IdentityInfo.Privilege,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)), connect.Token);
        if (args[0] == "--broker") {
            if (!IdentityInfo.Elevated) return 1;
            // One immutable request. An elevated supervisor owns the job and kills its child on cancellation/deadline.
            using var receive = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var request = await Wire.Read<WorkerRequest>(pipe, receive.Token);
            if (request.Command.Length > 32768 || request.Seconds is < 1 or > 60) return 1;
            using var worker = new Session(); using var cancelled = new CancellationTokenSource();
            _ = Task.Run(async () => { try { byte[] b = new byte[1]; await pipe.ReadAsync(b); } catch { } cancelled.Cancel(); });
            var result = await worker.Run(request.Command, request.Seconds, cancelled.Token);
            await Wire.Send(pipe, result, CancellationToken.None); return 0;
        }
        using var space = RunspaceFactory.CreateRunspace(InitialSessionState.CreateDefault2()); space.Open();
        space.SessionStateProxy.Path.SetLocation(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        while (true) {
            var request = await Wire.Read<WorkerRequest>(pipe, CancellationToken.None);
            if (request.Command.Length > 32768 || request.Seconds is < 1 or > 60) return 1;
            var result = await Execute(space, request.Command);
            await Wire.Send(pipe, result, CancellationToken.None);
        }
    }
    internal static async Task<Execution> Execute(Runspace space, string command)
    {
        using var shell = PowerShell.Create(); shell.Runspace = space;
        if (command == "Get-Date") {
            // Pinned cmdlet; prior scripts cannot replace this green operation with a function/alias.
            shell.AddCommand(new CmdletInfo("Get-Date", typeof(Microsoft.PowerShell.Commands.GetDateCommand)));
            var dates = await shell.InvokeAsync();
            return new("completed", ((DateTime)dates[0].BaseObject).ToString("O"), Directory: space.SessionStateProxy.Path.CurrentLocation.Path);
        }
        var output = new BoundedText(); var errors = new BoundedText(); bool hadErrors = false;
        using var values = new PSDataCollection<PSObject>();
        values.DataAdded += (_, _) => { foreach (var v in values.ReadAll()) output.Add(v?.ToString() ?? ""); };
        shell.Streams.Error.DataAdded += (_, _) => { hadErrors = true; foreach (var e in shell.Streams.Error.ReadAll()) errors.Add(e.ToString()); };
        shell.Streams.Warning.DataAdded += (_, _) => { foreach (var e in shell.Streams.Warning.ReadAll()) errors.Add("WARNING: " + e.Message); };
        shell.Streams.Information.DataAdded += (_, _) => { foreach (var e in shell.Streams.Information.ReadAll()) output.Add(e.MessageData?.ToString() ?? ""); };
        shell.Streams.Verbose.DataAdded += (_, _) => { foreach (var e in shell.Streams.Verbose.ReadAll()) output.Add("VERBOSE: " + e.Message); };
        shell.Streams.Debug.DataAdded += (_, _) => { foreach (var e in shell.Streams.Debug.ReadAll()) output.Add("DEBUG: " + e.Message); };
        shell.Streams.Progress.DataAdded += (_, _) => shell.Streams.Progress.Clear();
        // false preserves variables/functions across calls. Profiles are never loaded.
        shell.AddScript(command, useLocalScope: false).AddCommand("Microsoft.PowerShell.Utility\\Out-String").AddParameter("Stream").AddParameter("Width", 200);
        var previousOut = Console.Out; var previousError = Console.Error;
        Console.SetOut(new CaptureWriter(output)); Console.SetError(new CaptureWriter(errors));
        try { await Task.Factory.FromAsync(shell.BeginInvoke<PSObject, PSObject>(null, values), shell.EndInvoke); }
        catch (Exception e) { hadErrors = true; errors.Add(e.Message); }
        finally { Console.SetOut(previousOut); Console.SetError(previousError); }
        bool healthy = space.RunspaceStateInfo.State == RunspaceState.Opened;
        string directory = healthy ? space.SessionStateProxy.Path.CurrentLocation.Path : "unknown";
        return new(hadErrors || shell.HadErrors ? "failed" : "completed", output.Text, errors.Text,
            Directory: directory, Healthy: healthy, Truncated: output.Truncated || errors.Truncated);
    }
}
internal sealed class BoundedText
{
    private readonly StringBuilder text = new(); private readonly object sync = new();
    public bool Truncated { get; private set; }
    public void Add(string value, bool newline = true) { lock(sync) { int remaining = 65536 - text.Length;
        if (value.Length + (newline ? 1 : 0) > remaining) Truncated = true;
        if (remaining > 0) text.Append(value.AsSpan(0, Math.Min(value.Length, remaining)));
        if (newline && text.Length < 65536) text.Append('\n'); } }
    public string Text { get { lock(sync) return text.ToString(); } }
}

internal sealed class CaptureWriter(BoundedText output) : TextWriter {
    public override Encoding Encoding => Encoding.UTF8;
    public override void Write(char value) => output.Add(value.ToString(), false);
    public override void Write(string? value) { if(value != null) output.Add(value, false); }
    public override void Write(char[] buffer, int index, int count) => output.Add(new string(buffer,index,count), false);
}
