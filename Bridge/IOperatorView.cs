namespace PersistentBridge;

internal interface IOperatorView
{
    event Action? TaskStopped;
    event Action? NewTaskRequested;
    void Notify(string message);
    void SetState(string message);
    void Record(HistoryEntry entry);
    Task<Decision> Approve(Operation op, CancellationToken token,long start=0);
    Task<Credential?> RequestCredential(string target,bool sudo,CancellationToken token);
}
