using PersistentBridge;
Environment.SetEnvironmentVariable(ShellProfile.OverrideVariable,"powershell"); // this suite covers the PowerShell worker backend
if(args.FirstOrDefault() is "--worker" or "--broker")return Worker.Entry(args);
using var session=new Session();
async Task Check(string name,string script,Func<Execution,bool> verify,int seconds=10) {
    var result=await session.Run(script,seconds,CancellationToken.None);
    if(!verify(result))throw new Exception(name+" failed: "+result.Status+" "+result.Error);
    Console.WriteLine("PASS "+name);
}
await Check("set persistent variable","$DemoValue=123",r=>r.Status=="completed");
await Check("second request same worker","$DemoValue",r=>r.Output.Trim()=="123");
await Check("pinned date cmdlet","Get-Date",r=>DateTime.TryParse(r.Output,out _));
if(args.Contains("--measure")) {
    var pid=int.Parse((await session.Run("$PID",10,CancellationToken.None)).Output.Trim());
    using var worker=System.Diagnostics.Process.GetProcessById(pid);
    await Task.Delay(10000);
    var watch=System.Diagnostics.Stopwatch.StartNew();var before=worker.TotalProcessorTime;
    await Task.Delay(10000);worker.Refresh();
    Console.WriteLine($"Worker idle measurement: {100*(worker.TotalProcessorTime-before).TotalSeconds/watch.Elapsed.TotalSeconds:F2}% of one CPU, RSS {worker.WorkingSet64/1048576.0:F1} MiB, {watch.Elapsed.TotalSeconds:F1}s sample.");
}
await Check("timeout invalidates worker","Start-Sleep -Seconds 6",r=>!r.Healthy && r.Status=="timed_out",1);
return 0;
#if LINUX_HOST
namespace PersistentBridge {
    internal sealed class Window {
        public Task<Credential?> Credential(string target,bool sudo,CancellationToken token)=>throw new NotSupportedException();
    }
}
#endif
