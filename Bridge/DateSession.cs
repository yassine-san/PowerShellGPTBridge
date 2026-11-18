using System.Globalization;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using Microsoft.PowerShell.Commands;

namespace PersistentBridge;

internal sealed class DateSession : IDisposable
{
    private readonly Runspace space;
    public Guid Id { get; } = Guid.NewGuid();
    public string Directory { get; }
    private volatile bool quarantined;
    public bool Healthy => !quarantined && space.RunspaceStateInfo.State == RunspaceState.Opened;

    public DateSession()
    {
        // Pin the actual cmdlet implementation. No script profiles, module lookup or aliases.
        var state = InitialSessionState.Create();
        state.Commands.Add(new SessionStateCmdletEntry("Get-Date", typeof(GetDateCommand), null));
        state.Providers.Add(new SessionStateProviderEntry("FileSystem", typeof(FileSystemProvider), null));
        space = RunspaceFactory.CreateRunspace(state);
        space.Open();
        space.SessionStateProxy.Path.SetLocation(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        Directory = space.SessionStateProxy.Path.CurrentLocation.Path;
    }

    public async Task<(string Status, string Output, string Error)> Run(string command, int seconds, CancellationToken stop)
    {
        if (command != "Get-Date" || !Healthy) throw new InvalidOperationException("Command or session unavailable");
        stop.ThrowIfCancellationRequested();
        var shell = PowerShell.Create();
        shell.Runspace = space;
        shell.AddCommand(command);
        Task<PSDataCollection<PSObject>>? invocation = null;
        try
        {
            invocation = shell.InvokeAsync();
            var values = await invocation.WaitAsync(TimeSpan.FromSeconds(seconds), stop);
            // The only admitted cmdlet emits exactly one DateTime and launches no child processes.
            var output = values.Count == 1 && values[0].BaseObject is DateTime date
                ? date.ToString("O", CultureInfo.InvariantCulture) : "";
            var error = shell.Streams.Error.FirstOrDefault()?.ToString() ?? "";
            return (shell.HadErrors || output == "" ? "failed" : "completed", output, Clip(error));
        }
        catch (Exception e) when (e is TimeoutException or OperationCanceledException)
        {
            quarantined = true;
            // Do not dispose a still-running pipeline or block the MCP response on a stop.
            _ = Task.Run(async () =>
            {
                try { await shell.StopAsync(null, null); } catch { }
                try { if (invocation != null) await invocation; } catch { }
                shell.Dispose();
            });
            return (e is TimeoutException ? "timed_out" : "interrupted", "", "Session quarantined. Disconnect and restart.");
        }
        catch (Exception e)
        {
            quarantined = true;
            shell.Dispose();
            return ("failed", "", Clip(e.Message));
        }
        finally { if (!quarantined) shell.Dispose(); }
    }

    private static string Clip(string text) => text.Length <= 4096 ? text : text[..4095] + "…";
    public void Dispose() => space.Dispose();
}
