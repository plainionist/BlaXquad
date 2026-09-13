using squad.AgentProvider.Abstractions;
using squad.AgentProvider.Abstractions.Agents;
using squad.Domain;
using squad.Ui.Abstractions;
using System.Text.Json;

namespace squad.Application;

/// <summary>
/// The process-lifetime UI and application port. It is created once, before any squad generation exists, and
/// outlives every generation: the hosting layer subscribes to it for the whole process lifetime while Headquarters
/// installs the currently active <see cref="Squad"/> generation behind it.
///
/// Every query, command, and publication is bound to one generation: a call captures the installed generation
/// atomically and reaches only that generation, a generation that has since been retired rejects the call exactly
/// as a shutting-down squad does, and a publication is forwarded only while the generation that produced it is
/// still installed. With no generation installed the port answers as an empty, memberless squad.
/// </summary>
public sealed class SquadViewModel : ISquadUi, ITranscriptUi, ISquadPublication
{
    private readonly object myInstallationLock = new();
    private Squad? myInstalled;
    // Process-level command admission, closed once by Headquarters when the process begins releasing its
    // resources. A generation closes its own admission independently when it retires.
    private volatile bool myAccepting = true;

    public event Action<UiRefreshPriority>? SnapshotRequested;
    public event Action<TranscriptUpdate>? TranscriptChanged;

    /// <summary>
    /// Installs a generation as the one this port publishes, replacing any generation previously installed.
    /// Headquarters serializes installation, so this is never reached concurrently for two generations.
    /// </summary>
    public void Install(Squad squad)
    {
        lock (myInstallationLock)
            myInstalled = squad;
        NotifyStateChanged(UiRefreshPriority.Immediate);
    }

    /// <summary>
    /// Empties the active-squad slot when the named generation is the installed one, leaving this port answering as
    /// an empty squad until a replacement is installed. A retired generation that is still installed keeps
    /// answering queries and rejecting commands, which is what a stopping process needs.
    /// </summary>
    public void Uninstall(SquadGenerationId generation)
    {
        lock (myInstallationLock)
        {
            if (myInstalled?.Generation != generation)
            {
                return;
            }
            myInstalled = null;
        }
        NotifyStateChanged(UiRefreshPriority.Immediate);
    }

    /// <summary>Closes process-level command admission. New commands are rejected even with no generation installed.</summary>
    public void BeginStopping() => myAccepting = false;

    public JsonElement CreateSnapshot() =>
        Installed?.CreateSnapshot() ?? Squad.CreateEmptySnapshot();

    public IReadOnlyList<RoleTranscriptSnapshot> CreateTranscriptSnapshot(int maxEntriesPerRole)
    {
        Contract.Requires(maxEntriesPerRole > 0, "maxEntriesPerRole must be positive.");
        return Installed?.CreateTranscriptSnapshot(maxEntriesPerRole) ?? [];
    }

    public RoleTranscriptPage CreateTranscriptPage(SquadMemberId memberId, int beforeIndex, int maxEntries)
    {
        Contract.Requires(beforeIndex >= 0, "beforeIndex must not be negative.");
        Contract.Requires(maxEntries > 0, "maxEntries must be positive.");
        return RequireInstalled(memberId).CreateTranscriptPage(memberId, beforeIndex, maxEntries);
    }

    public RoleArchivedTranscriptEntry CreateArchivedTranscriptEntry(SquadMemberId memberId, int entryIndex)
    {
        Contract.Requires(entryIndex >= 0, "entryIndex must not be negative.");
        return RequireInstalled(memberId).CreateArchivedTranscriptEntry(memberId, entryIndex);
    }

    public AgentElicitationRequest GetPendingElicitation(SquadMemberId memberId, InteractionRequestId requestId) =>
        RequireInstalled(memberId).GetPendingElicitation(memberId, requestId);

    /// <summary>
    /// Returns the tri-state local readiness result derived purely from the installed generation's already-projected
    /// session, prompt, idle, stopped, and failed events - the only readiness source `wait-for-agent` observes.
    /// </summary>
    public Task<bool?> GetRoleReadinessAsync(SquadMemberId memberId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Installed?.GetRoleReadiness(memberId));

    public Task SendAsync(SquadMemberId memberId, string prompt, CancellationToken cancellationToken = default) =>
        RouteAsync(memberId, squad => squad.SendAsync(memberId, prompt, cancellationToken));

    public Task SendHarnessAsync(string role, string prompt, CancellationToken cancellationToken = default) =>
        RouteAsync(new SquadMemberId(role), squad => squad.SendHarnessAsync(new SquadMemberId(role), prompt, cancellationToken));

    public Task AbortAsync(SquadMemberId memberId, CancellationToken cancellationToken = default) =>
        RouteAsync(memberId, squad => squad.AbortAsync(memberId, cancellationToken));

    public Task CompletePermissionAsync(SquadMemberId memberId, InteractionRequestId requestId, bool approved, CancellationToken cancellationToken = default) =>
        RouteAsync(memberId, squad => squad.CompletePermissionAsync(memberId, requestId, approved, cancellationToken));

    public Task CompleteInputAsync(SquadMemberId memberId, InteractionRequestId requestId, string? answer, bool wasFreeform, CancellationToken cancellationToken = default) =>
        RouteAsync(memberId, squad => squad.CompleteInputAsync(memberId, requestId, answer, wasFreeform, cancellationToken));

    public Task CompleteElicitationAsync(SquadMemberId memberId, InteractionRequestId requestId, ElicitationAction action, JsonElement? content, CancellationToken cancellationToken = default) =>
        RouteAsync(memberId, squad => squad.CompleteElicitationAsync(memberId, requestId, action, content, cancellationToken));

    void ISquadPublication.NotifyStateChanged(SquadGenerationId generation, UiRefreshPriority priority)
    {
        if (Installed?.Generation != generation)
        {
            return;
        }
        NotifyStateChanged(priority);
    }

    void ISquadPublication.PublishTranscriptUpdate(SquadGenerationId generation, TranscriptUpdate update)
    {
        if (Installed?.Generation != generation)
        {
            return;
        }
        TranscriptChanged?.Invoke(update);
    }

    private Squad? Installed
    {
        get { lock (myInstallationLock) return myInstalled; }
    }

    /// <summary>
    /// Rejects a new command once the process has begun stopping, then routes it to the installed generation. A
    /// generation that has since retired rejects the command itself, so a stale in-flight call can never reach into
    /// a torn-down generation.
    /// </summary>
    private async Task RouteAsync(SquadMemberId memberId, Func<Squad, Task> command)
    {
        EnsureAccepting();
        await command(RequireInstalled(memberId));
    }

    private Squad RequireInstalled(SquadMemberId memberId) =>
        Installed ?? throw new InvalidOperationException($"Unknown role: {memberId}");

    private void EnsureAccepting()
    {
        if (!myAccepting)
        {
            throw new OperationCanceledException("Squad is shutting down");
        }
    }

    private void NotifyStateChanged(UiRefreshPriority priority)
    {
        foreach (Action<UiRefreshPriority> listener in SnapshotRequested?.GetInvocationList() ?? [])
        {
            try
            {
                listener(priority);
            }
            catch
            {
            }
        }
    }
}
