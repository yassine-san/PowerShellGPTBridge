using System.Diagnostics;
namespace PersistentBridge;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try {
            Environment.SetEnvironmentVariable("CONTROL_PLANE_API_KEY",null);
#if WINDOWS
            if (args.FirstOrDefault() is "--worker" or "--broker") return Worker.Entry(args);
            DesktopAccess.Check();
            if (IdentityInfo.Elevated && Environment.GetEnvironmentVariable("PSBRIDGE_ALLOW_ELEVATED_GET_DATE")!="1" &&
                Environment.GetEnvironmentVariable("PSBRIDGE_ALLOW_ELEVATED_GENERAL")!="1" && !args.SequenceEqual(["--ui-test"]))
                throw new InvalidOperationException("Use a standard desktop or explicitly opt in to elevated execution.");
#else
            LinuxOS.umask(0x3f);
            if (!OperatingSystem.IsLinux() || IdentityInfo.Elevated) throw new InvalidOperationException("Start as an unprivileged Linux desktop user.");
            if (args.FirstOrDefault()=="--worker") return Worker.Entry(args);
            if (!LinuxOS.Graphical) throw new InvalidOperationException("An interactive graphical session is required.");
#endif
            bool test=args.SequenceEqual(["--ui-test"]);
            if (!test && args.Length!=0) throw new ArgumentException("Unknown startup option.");
#if WINDOWS
            using var parent=Process.GetProcessById(WindowsParent.Id());
            string executableName="tunnel-client.exe";
#else
            using var parent=Process.GetProcessById(LinuxOS.getppid());
            string executableName="tunnel-client";
#endif
            if (!test) {
                string? path=Environment.GetEnvironmentVariable("PSBRIDGE_TUNNEL_EXE");
                if (path==null || !Path.IsPathFullyQualified(path) || Path.GetFileName(path)!=executableName ||
                    !string.Equals(parent.MainModule?.FileName,path,OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal))
                    throw new InvalidOperationException("Use the launcher and official tunnel client.");
            }
            using var shutdown=new CancellationTokenSource();
            var audit=new Audit();
#if WINDOWS
            using var guard=new Mutex(false,@"Local\PersistentPSBridge-"+System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value,out bool first);
            if(!first)throw new IOException("Another bridge is running.");
#endif
            using var lease=new FileStream(Path.Combine(audit.Root,"instance.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            using var window=new Window(audit.Root,shutdown);
            await window.Start(shutdown.Token);
            Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;shutdown.Cancel();};
            if (test) {
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var op=new Operation(Guid.NewGuid(),Environment.MachineName,"Local UI test",IdentityInfo.Name,IdentityInfo.Privilege,
                    "Get-Date",Environment.CurrentDirectory,10,"Inspect the approval window","Display current date","UI test only",true,"Clock",Guid.NewGuid());
                var decision=await window.Approve(op,timeout.Token);
                Console.Error.WriteLine($"UI test decision: {(decision.Approved?"approved":decision.Status)}; no operation executed.");
                return 0;
            }
            using var endpoint=new Endpoint(window,audit,shutdown.Token);
            window.ResetRequested+=endpoint.Reset;
            window.SetState(endpoint.Summary);
            endpoint.Started();
            _=parent.WaitForExitAsync(shutdown.Token).ContinueWith(t=>{if(t.IsCompletedSuccessfully) shutdown.Cancel();},TaskScheduler.Default);
            try { await endpoint.Serve(); } finally { shutdown.Cancel(); }
            return 0;
        } catch (OperationCanceledException) { return 0; }
        catch (Exception ex) { Console.Error.WriteLine("Bridge stopped: "+ex.Message); return 1; }
    }
}
