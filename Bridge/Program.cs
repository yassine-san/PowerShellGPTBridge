using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace PersistentBridge;

internal static class Program
{
    // The Windows kernel returns the creator PID in PROCESS_BASIC_INFORMATION.
    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int infoClass,
        [Out] IntPtr[] info, int length, out int returned);

    private static Process TunnelParent()
    {
        var info = new IntPtr[6];
        using var self = Process.GetCurrentProcess();
        if (NtQueryInformationProcess(self.Handle, 0, info, 6 * IntPtr.Size, out _) != 0)
            throw new InvalidOperationException("Could not identify tunnel parent.");
        var parent = Process.GetProcessById(info[5].ToInt32());
        var expected = Environment.GetEnvironmentVariable("PSBRIDGE_TUNNEL_EXE");
        if (expected == null || !Path.IsPathFullyQualified(expected) ||
            !string.Equals(parent.MainModule?.FileName, expected, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(expected), "tunnel-client.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Start via Start-Bridge.ps1 and the official tunnel client.");
        return parent;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        var stage = "identity";
        try
        {
            Environment.SetEnvironmentVariable("CONTROL_PLANE_API_KEY", null);
            if (args.Length > 0 && args[0] is "--worker" or "--broker") return Worker.Entry(args);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            using var user = WindowsIdentity.GetCurrent();
            var elevated = new WindowsPrincipal(user).IsInRole(WindowsBuiltInRole.Administrator);
            Console.Error.WriteLine($"[bridge supervised-0.3] exe={Environment.ProcessPath} assembly={typeof(Program).Assembly.Location} mvid={typeof(Program).Module.ModuleVersionId}");
            Console.Error.WriteLine($"runtime={Environment.Version} session={Process.GetCurrentProcess().SessionId} admin={elevated} environmentInteractive={Environment.UserInteractive} formsInteractive={SystemInformation.UserInteractive} stdinRedirected={Console.IsInputRedirected} stdoutRedirected={Console.IsOutputRedirected}");
            var localTest = args.SequenceEqual(new[] { "--ui-test" });
            if (args.Length != 0 && !localTest) throw new ArgumentException("Only --ui-test is supported.");
            stage = "desktop";
            DesktopAccess.Check();
            if (localTest)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using var testWindow = new Window();
                testWindow.State.Text = "Local UI test · no PowerShell execution";
                testWindow.Shown += async (_, _) =>
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                    var operation = new Operation(Guid.NewGuid(), Environment.MachineName, "Local UI test", IdentityInfo.Name,
                        IdentityInfo.Privilege, "Get-Date", Environment.CurrentDirectory, 10, "Inspect the local approval window",
                        "Return the current date in a live call", "This test does not execute the command.", true, "System clock", Guid.NewGuid());
                    var decision = await testWindow.Approve(operation, timeout.Token);
                    Console.Error.WriteLine($"UI test decision={(decision.Approved ? "approved" : decision.Status)} feedbackLength={decision.Reason.Length}; command was not executed.");
                    testWindow.Close();
                };
                Application.Run(testWindow);
                return 0;
            }
            stage = "singleton";
            using var singleton = new Mutex(true, @"Local\PersistentPSBridge-" + user.User!.Value, out var first);
            if (!first) throw new InvalidOperationException("A bridge is already running in this logon session.");
            stage = "tunnel-parent";
            using var parent = TunnelParent();
            Console.Error.WriteLine($"parentPid={parent.Id} parentPath={parent.MainModule?.FileName}");
            stage = "ui-and-session";
            using var shutdown = new CancellationTokenSource();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var audit = new Audit();
            using var window = new Window(audit.Root);
            using var endpoint = new Endpoint(window, audit, shutdown.Token);
            endpoint.Started();
            window.Reset.Click += (_, _) =>
            {
                try { endpoint.Reset(); }
                catch (Exception e) { MessageBox.Show("Reset failed: " + e.Message); window.Close(); }
            };
            window.State.Text = endpoint.Summary;
            Task? serving = null;
            window.FormClosing += (_, _) => shutdown.Cancel();
            window.Shown += (_, _) =>
            {
                Console.Error.WriteLine("Windows Forms shown; starting MCP stdio.");
                _ = TunnelStatus.Watch(window, shutdown.Token);
                serving = Task.Run(async () =>
                {
                    try { await endpoint.Serve(); }
                    catch (Exception e) when (!shutdown.IsCancellationRequested) { Console.Error.WriteLine("MCP stopped: " + e.GetType().Name); }
                    finally
                    {
                        shutdown.Cancel();
                        if (!window.IsDisposed && window.IsHandleCreated)
                            try { window.BeginInvoke((Action)window.Close); } catch (InvalidOperationException) { }
                    }
                });
                _ = Task.Run(async () =>
                {
                    try { await parent.WaitForExitAsync(shutdown.Token); }
                    catch (OperationCanceledException) { return; }
                    shutdown.Cancel();
                    try { window.BeginInvoke((Action)window.Close); } catch (InvalidOperationException) { }
                });
            };
            stage = "message-loop";
            Console.Error.WriteLine("Entering Windows Forms message loop.");
            Application.Run(window);
            shutdown.Cancel();
            try { serving?.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
            audit.Write("session_ended", new { reason = "Disconnect or transport closure. State discarded." });
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine($"Startup/runtime failure stage={stage}: {e}"); return 1; }
    }
}
