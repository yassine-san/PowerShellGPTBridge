using System.Diagnostics;
namespace PersistentBridge;

internal sealed class PowerShellWorkerSession(bool elevated=false) : ISessionBackend
{
    private readonly object sync=new();
    private Process? child;
    public Guid Id { get; }=Guid.NewGuid();
    public string Directory { get; private set; }=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public bool Healthy { get; private set; }=true;
    public async Task<Execution> Run(string command,int seconds,CancellationToken stop)
    {
        Process worker;bool fresh;
        lock(sync) {
            stop.ThrowIfCancellationRequested();
            if(elevated || !Healthy) throw new InvalidOperationException("Session unavailable; local elevation is unsupported.");
            fresh=child==null;
            if(fresh) {
                var start=new ProcessStartInfo(Environment.ProcessPath!) {UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
                start.ArgumentList.Add("--worker");start.ArgumentList.Add(Environment.ProcessId.ToString());
                foreach(var k in start.Environment.Keys.Where(k=>k.StartsWith("PSBRIDGE_")||k=="CONTROL_PLANE_API_KEY").ToArray()) start.Environment.Remove(k);
                child=Process.Start(start)??throw new IOException("Worker failed to start.");
                _=child.StandardError.BaseStream.CopyToAsync(Stream.Null);
            }
            worker=child!;
        }
        try {
            if(fresh) {
                using var startup=CancellationTokenSource.CreateLinkedTokenSource(stop);
                startup.CancelAfter(TimeSpan.FromSeconds(20));
                var hello=await Wire.Read<WorkerHello>(worker.StandardOutput.BaseStream,startup.Token);
                if(hello.Identity!=IdentityInfo.Name||hello.Privilege!=IdentityInfo.Privilege) throw new IOException("Worker identity changed.");
            }
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(stop);
            deadline.CancelAfter(TimeSpan.FromSeconds(seconds));
            await Wire.Send(worker.StandardInput.BaseStream,new WorkerRequest(command,seconds),deadline.Token);
            var result=await Wire.Read<Execution>(worker.StandardOutput.BaseStream,deadline.Token);
            Directory=result.Directory;
            if(!result.Healthy)Dispose();
            return result;
        } catch(OperationCanceledException) {
            Dispose();return new(stop.IsCancellationRequested?"cancelled":"timed_out",Error:"Worker stopped. Session state lost; side effects may remain.",Directory:Directory,Healthy:false);
        } catch {
            Dispose();return new("session_unhealthy",Error:"Worker disconnected. Execution outcome unknown.",Directory:Directory,Healthy:false);
        }
    }
    public void Dispose()
    {
        lock(sync) {
            Healthy=false;
            if(child==null)return;
            try {LinuxOS.kill(-child.Id,9);if(!child.HasExited)child.Kill(true);}catch(InvalidOperationException){}
            finally{child.Dispose();child=null;}
        }
    }
}
