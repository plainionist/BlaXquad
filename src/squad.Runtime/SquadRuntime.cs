using squad.AgentProvider.Abstractions;
using squad.Application;
using squad.Domain;
using squad.Handoffs.Delivery;

namespace squad.Runtime;

/// <summary>
/// One replaceable squad generation and the sole owner of every resource whose lifetime follows it: its generation
/// identity and configuration snapshot, its ordered member directory and processors with all transient member
/// state, its command admission, its backend runtime, member sessions and session observers, and its handoff-pump
/// participation. Headquarters owns the process shell and at most one of these generations; it never dismantles
/// these resources in individual steps but retires the whole generation through <see cref="RetireAsync"/>.
/// </summary>
internal sealed class SquadRuntime
{
    private static readonly Task myNever = Task.Delay(Timeout.InfiniteTimeSpan);
    private readonly SquadMembers myMembers;
    private readonly IAgentBackend myAgentBackend;
    private readonly InProcessHandoffPoller myHandoffPump;
    private readonly SessionGeneration mySessions;
    private readonly Func<CancellationToken, Task> mySessionsStarted;
    private readonly CancellationTokenSource myStopping = new();
    private readonly object myRetirementLock = new();
    private Task<SquadRetirement>? myRetirement;
    private bool myHandoffStarted;
    private bool myStarted;

    internal SquadRuntime(
        SquadMembers members,
        IAgentBackend agentBackend,
        IReadOnlyList<SquadMemberDefinition> handoffMembers,
        string handoffLogPath,
        Func<CancellationToken, Task> sessionsStarted)
    {
        myMembers = members;
        myAgentBackend = agentBackend;
        myHandoffPump = new InProcessHandoffPoller(
            handoffMembers, new SessionRoleNotifier(members), new HandoffDeliveryLog(handoffLogPath));
        mySessions = new SessionGeneration(agentBackend, members, myStopping.Token);
        mySessionsStarted = sessionsStarted;
        BackendFailure = (agentBackend as IAgentBackendFailureSource)?.Failure ?? myNever;
    }

    internal SquadGenerationId Generation => myMembers.Generation;

    /// <summary>This generation's member directory, published by the process-lifetime UI port while installed.</summary>
    internal SquadMembers Members => myMembers;

    /// <summary>A fatal, backend-wide provider failure independent of any single member's session.</summary>
    internal Task BackendFailure { get; }

    /// <summary>An unexpected termination of this generation's handoff polling loop.</summary>
    internal Task HandoffFailure => myHandoffPump.Failure;

    /// <summary>
    /// The one cohesive start operation: establishes this generation's member sessions, announces them to the
    /// process shell, then joins handoff delivery. A failure leaves the generation owned and retirable.
    /// </summary>
    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        Contract.Invariant(!myStarted, "A squad cannot start more than once.");
        Contract.Invariant(myRetirement is null, "A squad cannot start after retirement has begun.");
        myStarted = true;
        await mySessions.StartAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await mySessionsStarted(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await myHandoffPump.StartAsync(cancellationToken);
        myHandoffStarted = true;
    }

    /// <summary>
    /// The one idempotent, failure-collecting retirement operation. Closes command admission first, stops handoff
    /// participation, cancels and drains this generation's processors and observers, then retires the provider
    /// runtime. Every resource whose termination is uncertain stays owned by this generation and the result reports
    /// the retirement as non-conclusive, so Headquarters cannot start a replacement over it; a conclusive
    /// retirement additionally releases the backend and the per-member synchronization primitives. Retirement is
    /// attempted exactly once and its result is final, so repeated calls neither repeat teardown work nor report a
    /// failure twice.
    /// </summary>
    internal Task<SquadRetirement> RetireAsync()
    {
        lock (myRetirementLock)
            return myRetirement ??= RetireCoreAsync();
    }

    private async Task<SquadRetirement> RetireCoreAsync()
    {
        var failures = new List<Exception>();
        myStopping.Cancel();
        myMembers.CloseAdmission();
        if (myHandoffStarted)
        {
            await AttemptAsync(() => myHandoffPump.StopAsync(), failures);
        }
        await AttemptAsync(myMembers.DrainAsync, failures);
        myMembers.Retire();
        failures.AddRange(await mySessions.TeardownAsync());
        // The pump's own termination is certain once its stop has been awaited and any failure collected, so it is
        // released even when the provider runtime's retirement is not.
        await AttemptAsync(() => myHandoffPump.DisposeAsync().AsTask(), failures);
        if (failures.Count > 0)
        {
            return new SquadRetirement(false, failures);
        }
        await AttemptAsync(() => myAgentBackend.DisposeAsync().AsTask(), failures);
        if (failures.Count > 0)
        {
            return new SquadRetirement(false, failures);
        }
        myMembers.Dispose();
        myStopping.Dispose();
        return SquadRetirement.Conclusive;
    }

    private static async Task AttemptAsync(Func<Task> action, ICollection<Exception> failures)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }
}
