using PersistentBridge;
using var endpoint = new Endpoint(new Window(), new Audit(), CancellationToken.None);
await endpoint.Serve();
namespace PersistentBridge {
internal static class IdentityInfo { public static bool Elevated => Environment.GetEnvironmentVariable("TEST_ELEVATED") == "1"; public static string Name => "test-user"; public static string Privilege => Elevated ? "ELEVATED — ADMINISTRATOR" : "STANDARD USER"; }
internal class Control { public string Text { get; set; } = ""; public event EventHandler? Click; public void Invoke() => Click?.Invoke(this,EventArgs.Empty); }
internal class Window {
 public Control State = new(), NewTask = new(); public event Action? TaskStopped;
 public void Record(HistoryEntry entry) { }
 public async Task<Decision> Approve(Operation op, CancellationToken token, long start = 0) { await Task.Delay(100,token);
 Console.Error.WriteLine("APPROVAL:"+op.Command);
 if(op.Command=="clock-expiry") { try { await Task.Delay(Timeout.Infinite,token); } catch(OperationCanceledException) { return new(false,"unfinished",Status:"approval_expired"); } }
 if(op.Command=="late-approval") { await Task.Delay(TimeSpan.FromSeconds(46),token); return new(true); }
 if(op.Command=="expired") return new(false,"half a sentence",Status:"approval_expired");
 if(op.Command=="stop") {TaskStopped?.Invoke(); return new(false,"Stopped locally",true);}
 if(op.Command=="reject") return new(false,"Keep the backup chain");
 if(op.Command=="ui-failure") throw new InvalidOperationException("UI unavailable");
 return new(true);
 }
}
internal class Audit { public void Write(string kind, object data) { } public object Command(string? value)=>value ?? ""; }
internal static class MessageBox { public static void Show(string text) { } }
internal sealed class Session(bool elevated=false) : IDisposable {
 public Guid Id {get;}=Guid.NewGuid(); public string Directory=>"/test"; public bool Healthy {get;private set;}=true;
 static int executions;
 public Task<Execution> Run(string command,int seconds,CancellationToken token) { token.ThrowIfCancellationRequested(); Console.Error.WriteLine("EXECUTE:"+command); return Task.FromResult(new Execution("completed", (++executions).ToString(), Directory:Directory)); }
 public void Dispose()=>Healthy=false;
}
internal record Credential(string Username,string Secret);
internal static class CredentialStore { public static Task<Credential?> Prompt(Window w,string t,CancellationToken c,bool sudo=false)=>Task.FromResult<Credential?>(null); }
internal sealed class SshSessions(Window w,Audit a) : IDisposable {
 public string Key(string h,int p)=>h+":"+p; public string Identity(string k)=>"remote-test"; public bool Connected(string k)=>false; public Guid Id(string k)=>Guid.Empty;
 public Task<Credential?> Credential(string k,CancellationToken c)=>Task.FromResult<Credential?>(null);
 public Task Connect(string h,int p,Credential c,Guid id,Guid taskId,System.Diagnostics.Stopwatch budget,int seconds,CancellationToken ct)=>throw new NotSupportedException();
 public void Disconnect(string k){} public void Dispose(){}
 public Task<Execution> Transfer(string k,string a,string l,string r,int s,CancellationToken c,bool captureDiff = false)=>throw new NotSupportedException();
 public Task<Execution> Run(string k,string c,int s,CancellationToken ct,string? password)=>throw new NotSupportedException();
 public static string Sudo(string c)=>"sudo "+c;
}
}
