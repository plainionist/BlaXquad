namespace squad.AgentProvider.Abstractions;

/// <summary>
/// Atomically combines lifecycle admission with current-generation session lookup, preventing callers from
/// dispatching through a session retired by a concurrent lifecycle transition. Implementations own no I/O
/// resources; they only decide admission and issue leases.
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
