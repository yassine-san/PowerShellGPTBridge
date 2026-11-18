using System.Security.Principal;
namespace PersistentBridge;
internal sealed record Operation(Guid Id, string Target, string Connection, string Identity, string Privilege,
    string Command, string Directory, int Seconds, string Description, string Effects, string Risks, bool ReadOnly = false, string Resources = "Additional resources unknown", Guid TaskId = default, bool CanCaptureDiff = false);
internal sealed record Decision(bool Approved, string Reason = "", bool Stop = false, string Status = "rejected", bool CaptureDiff = false);
internal sealed record Execution(string Status, string Output = "", string Error = "", int? ExitCode = null,
    string Directory = "", bool Healthy = true, bool Truncated = false, string? FileDiff = null);

internal sealed record HistoryEntry(Operation Op, Guid TaskId, Guid SessionId, DateTimeOffset Requested,
    string Status = "requested", string Approval = "none", string Reason = "", DateTimeOffset? Decided = null,
    DateTimeOffset? Started = null, DateTimeOffset? Ended = null, long DurationMs = 0, Execution? Result = null,
    string Evidence = "Changes not independently verified.");

internal sealed class HostApprovalException(Guid id, Decision decision) : Exception("SSH host authorization did not complete")
{
    public Guid RequestId { get; } = id;
    public Decision Decision { get; } = decision;
}
