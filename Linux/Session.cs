namespace PersistentBridge;

/// <summary>Session used by Endpoint. Delegates to the backend that matches the detected shell.</summary>
internal sealed class Session : IDisposable
{
    private readonly ISessionBackend backend;
    public Session(bool elevated = false)
    {
        var profile = ShellProfile.Current;
        backend = profile.IsPowerShell ? new PowerShellWorkerSession(elevated) : new ShellSession(profile, elevated);
    }
    public Guid Id => backend.Id;
    public string Directory => backend.Directory;
    public bool Healthy => backend.Healthy;
    public Task<Execution> Run(string command, int seconds, CancellationToken stop) => backend.Run(command, seconds, stop);
    public void Dispose() => backend.Dispose();
}
