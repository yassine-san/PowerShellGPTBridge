namespace PersistentBridge;

/// <summary>A persistent local command session. One backend exists per shell (PowerShell worker, bash, zsh).</summary>
internal interface ISessionBackend : IDisposable
{
    Guid Id { get; }
    string Directory { get; }
    bool Healthy { get; }
    Task<Execution> Run(string command, int seconds, CancellationToken stop);
}
