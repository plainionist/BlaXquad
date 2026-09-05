namespace squad.AgentProvider.Abstractions;

/// <summary>
/// The narrow contract the lifecycle authority exposes to callers that dispatch work to a role's session, such as
/// <c>SquadViewModel</c> and handoff delivery. It combines the "are we still accepting work" phase check with the
/// generation-bound session lookup so the two can never race independently of each other or of a lifecycle
/// transition. Implementations own no I/O resource; they only decide admission and hand out leases.
/// </summary>
public interface ISessionAdmission
{
    /// <summary>
    /// True while the authority is still accepting new work (before a stopping transition has begun).
    /// </summary>
    bool IsAccepting { get; }

    /// <summary>
    /// Atomically checks whether the authority is still accepting work and, if so, returns the current-generation
    /// session leased for <paramref name="role"/>. Returns false if the authority is stopping, no session is
    /// registered for the role, or that session has already completed.
    /// </summary>
    bool TryLeaseSession(string role, out SessionLease lease);
}
