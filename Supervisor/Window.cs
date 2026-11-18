using System.Diagnostics;
using System.Text;
using System.Text.Json;
namespace PersistentBridge;

// The launched child exclusively owns these inherited stdio endpoints. MCP uses different streams.
internal sealed class Window(string root,CancellationTokenSource shutdown) : IDisposable, IOperatorView
{
    private readonly History history=new(root);
    private readonly Dictionary<Guid,HistoryEntry> records=new();
    private readonly Queue<Guid> completed=new();
    private readonly object sync=new();
    private Process? ui;
    private TaskCompletionSource ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource<JsonElement>? pending;
    private Guid ticket, id;
    private string draft="", kind="";
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public Task<Credential?> RequestCredential(string target,bool sudo,CancellationToken token) =>
        CredentialStore.Prompt(this,target,token,sudo);
    public event Action? TaskStopped,NewTaskRequested,ResetRequested;
    public void Notify(string text)=>Send(new {type="notice",text});
    public void SetState(string text)=>Send(new {type="session",text});
    public async Task Start(CancellationToken token)
    {
        var start=new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,OperatingSystem.IsWindows()?"bridge-supervisor.exe":"bridge-supervisor")) {
            RedirectStandardInput=true,RedirectStandardOutput=true,UseShellExecute=false,
            StandardInputEncoding=new UTF8Encoding(false),StandardOutputEncoding=Encoding.UTF8 };
        ui=Process.Start(start)??throw new IOException("Desktop could not start.");
        _=ReadUi();
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(20),token);
        foreach (var entry in history.Load()) { records[entry.Op.Id]=entry; }
        Send(new {type="init",identity=IdentityInfo.Name,privilege=IdentityInfo.Privilege,target=Environment.MachineName,
            rows=records.Values.Select(Summary).ToArray()});
    }
    private void Send(object message)
    {
        lock(sync) {
            if (ui==null || ui.HasExited) throw new IOException("Desktop unavailable.");
            ui.StandardInput.WriteLine(JsonSerializer.Serialize(message,Json));
            ui.StandardInput.Flush();
        }
    }
    private static object Summary(HistoryEntry e)=>new {e.Op.Id,title=e.Op.Description,e.Op.Target,e.Status,e.Approval,e.Requested,
        e.Op.ReadOnly,e.DurationMs,e.TaskId,e.SessionId,e.Started,e.Op.Seconds};
    public void Record(HistoryEntry e)
    {
        lock(sync) {
            history.Save(e);records[e.Op.Id]=e;
            if (e.Ended!=null) {
                completed.Enqueue(e.Op.Id);
                if(completed.Count>128) { var old=completed.Dequeue();records[old]=History.ForDisk(records[old]); }
            }
            Send(new {type="row",row=Summary(e)});
        }
    }
    private async Task ReadUi()
    {
        try {
            while(await ui!.StandardOutput.ReadLineAsync() is { } line) {
                if(line.Length>32768) throw new IOException("Invalid UI message.");
                using var doc=JsonDocument.Parse(line);var m=doc.RootElement;
                var type=m.GetProperty("type").GetString();
                switch(type) {
                    case "ready": ready.TrySetResult();break;
                    case "detail":
                        lock(sync) if(records.TryGetValue(m.GetProperty("id").GetGuid(),out var e)) Send(new {type="entry",entry=e});
                        break;
                    case "reset": ResetRequested?.Invoke();break;
                    case "newTask": NewTaskRequested?.Invoke();break;
                    case "stopTask": TaskStopped?.Invoke();break;
                    case "disconnect": return;
                    case "draft": case "decide": case "credentialReply":
                        lock(sync) {
                            if(pending==null || m.GetProperty("ticket").GetGuid()!=ticket || m.GetProperty("id").GetGuid()!=id) break;
                            if(type=="draft" && kind=="approval") draft=Feedback(m);
                            else if((type=="decide" && kind=="approval") || (type=="credentialReply" && kind=="credential")) pending.TrySetResult(m.Clone());
                        }
                        break;
                }
            }
        } catch(Exception ex) { Console.Error.WriteLine("Desktop channel closed: "+ex.GetType().Name); }
        finally { ready.TrySetException(new IOException("Desktop unavailable."));shutdown.Cancel(); }
    }
    private static string Feedback(JsonElement m)=>m.TryGetProperty("reason",out var p) && p.GetString() is {Length:<=2000} s?s:"";
    private async Task<JsonElement> Prompt(string type,Guid request,object payload,CancellationToken token)
    {
        Task<JsonElement> answer;
        lock(sync) {
            if(pending!=null) throw new InvalidOperationException("A local prompt is already open.");
            ticket=Guid.NewGuid();id=request;kind=type;draft="";pending=new(TaskCreationOptions.RunContinuationsAsynchronously);
            answer=pending.Task;
        }
        try {
            Send(new {type,id=request,ticket,payload});
            using var stop=CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token,token);
            return await answer.WaitAsync(stop.Token);
        } finally {
            lock(sync) { pending=null; if(ui is {HasExited:false}) Send(new {type="dismiss",id=request}); }
        }
    }
    public async Task<Decision> Approve(Operation op,CancellationToken token,long start=0)
    {
#if WINDOWS
        DesktopAccess.Check();
#endif
        if(start==0) start=Stopwatch.GetTimestamp();
        var left=TimeSpan.FromSeconds(60)-Stopwatch.GetElapsedTime(start);
        if(left<=TimeSpan.Zero) return new(false,Status:"approval_expired");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(left);
        try {
            Guid session;lock(sync) session=records.GetValueOrDefault(op.Id)?.SessionId??Guid.Empty;
            var m=await Prompt("approval",op.Id,new {op,sessionId=session,deadline=DateTimeOffset.UtcNow.Add(left).ToUnixTimeMilliseconds()},timeout.Token);
            if(timeout.IsCancellationRequested || shutdown.IsCancellationRequested || Stopwatch.GetElapsedTime(start)>=TimeSpan.FromSeconds(60)) return new(false,draft,Status:"approval_expired");
#if WINDOWS
            DesktopAccess.Check();
#endif
            string? choice=m.GetProperty("choice").GetString();
            if(choice=="stop") TaskStopped?.Invoke();
            return new(choice=="approve",choice=="approve"?"":m.TryGetProperty("reason",out _)?Feedback(m):draft,choice=="stop",choice=="stop"?"cancelled":"rejected",
                choice=="approve" && op.CanCaptureDiff && m.TryGetProperty("captureDiff",out var d) && d.ValueKind==JsonValueKind.True);
        } catch(OperationCanceledException) { return new(false,draft,Status:"approval_expired"); }
    }
    public async Task<Credential?> Credential(string target,bool sudo,CancellationToken token)
    {
        var m=await Prompt("credential",Guid.NewGuid(),new {target,sudo},token);
        if(!m.GetProperty("accepted").GetBoolean()) return null;
        return new(m.GetProperty("username").GetString()??"",m.GetProperty("method").GetString()??"password",
            m.GetProperty("secret").GetString()??"",m.GetProperty("path").GetString()??"",false);
    }
    public void Dispose() { if(ui!=null){try{ui.StandardInput.Close();if(!ui.WaitForExit(800))ui.Kill(true);}finally{ui.Dispose();}} }
}
