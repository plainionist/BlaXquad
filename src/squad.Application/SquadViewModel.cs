using squad.AgentProvider.Abstractions;
using squad.AgentProvider.Abstractions.Agents;
using squad.Application.Events;
using squad.Application.Interactions;
using squad.Application.RoleOperations;
using squad.Transcripts;
using squad.Ui.Abstractions;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Threading.Channels;

namespace squad.Application;

public sealed class SquadViewModel : ISquadUi, ITranscriptUi, IAsyncDisposable
{
    private readonly Channel<Func<Task>> myCommands = Channel.CreateUnbounded<Func<Task>>();
    private readonly CancellationTokenSource myShutdown = new();
    private readonly ConcurrentDictionary<string, IAgentSession> mySessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AgentRoleState> myRoles = new(StringComparer.Ordinal);
    private readonly TranscriptArchive myTranscriptArchive;
    private readonly TranscriptRetentionOptions myTranscriptRetentionOptions;
    private readonly RoleOperationCoordinator myRoleOperations = new();
    private readonly PendingInteractionRegistry myInteractions = new();
    private readonly AgentEventProjector myEventProjector;
    private readonly Task myEventLoop;
    private readonly object myAdmissionLock = new();
    private readonly HashSet<Task> myAcceptedCommands = [];
    private readonly StandaloneSessionAdmission myStandaloneAdmission;
    private ISessionAdmission myAdmission;

    public SquadViewModel()
        : this(new TranscriptRetentionOptions())
    {
    }

    public SquadViewModel(TranscriptRetentionOptions transcriptRetentionOptions)
    {
        ValidateTranscriptRetentionOptions(transcriptRetentionOptions);
        myTranscriptRetentionOptions = transcriptRetentionOptions;
        myTranscriptArchive = new TranscriptArchive(transcriptRetentionOptions);
        myEventProjector = new AgentEventProjector(myInteractions);
        myStandaloneAdmission = new StandaloneSessionAdmission(this);
        myAdmission = myStandaloneAdmission;
        myEventLoop = RunEventLoopAsync();
    }

    public ReadOnlyDictionary<string, AgentRoleState> Roles => new(myRoles);
    public IReadOnlyCollection<AgentPermissionRequest> PendingPermissions => myInteractions.Permissions;
    public IReadOnlyCollection<AgentInputRequest> PendingInputs => myInteractions.Inputs;
    public IReadOnlyCollection<AgentElicitationRequest> PendingElicitations => myInteractions.Elicitations;
    public string TranscriptHistoryDirectory => myTranscriptArchive.DirectoryPath;
    public event Action? StateChanged;
    public event Action<UiRefreshPriority>? SnapshotRequested;
    public event Action<TranscriptUpdate>? TranscriptChanged;

    public void InitializeRoles(IEnumerable<string> roleNames)
    {
        foreach (var role in roleNames)
            myRoles.TryAdd(role, new AgentRoleState(role, myTranscriptArchive, myTranscriptRetentionOptions));
        NotifyStateChanged();
    }

    public JsonElement CreateSnapshot()
    {
        var roles = myRoles.Values.Select(role => role.CreateSnapshot()).ToArray();
        return JsonSerializer.SerializeToElement(new
        {
            roles = roles.Select(role => new
            {
                role = role.Role,
                status = role.Status,
                lastEventAt = role.LastEventAt,
                error = role.Error,
                activeTool = role.ActiveTool,
                isWorking = role.IsWorking,
                model = role.Model,
                effort = role.Effort,
                aicUsed = role.AicUsed,
                contextUsedTokens = role.ContextUsedTokens,
                contextLimitTokens = role.ContextLimitTokens,
                eventCount = role.EventCount,
            }),
            permissions = PendingPermissions.Select(permission => new
            {
                requestId = permission.RequestId,
                role = permission.Role,
                description = permission.Description,
            }),
            inputs = PendingInputs.Select(input => new
            {
                requestId = input.RequestId,
                role = input.Role,
                prompt = input.Prompt,
                choices = input.Choices,
                allowFreeform = input.AllowFreeform,
            }),
            elicitations = PendingElicitations.Select(elicitation => new
            {
                requestId = elicitation.RequestId,
                role = elicitation.Role,
                prompt = elicitation.Prompt,
                mode = elicitation.Mode,
                requestedSchema = elicitation.RequestedSchema,
                url = elicitation.Url,
            }),
        });
    }

    public IReadOnlyList<RoleTranscriptSnapshot> CreateTranscriptSnapshot(int maxEntriesPerRole)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntriesPerRole);
        return myRoles.Values
            .Select(role => role.Transcript.CreateTranscriptSnapshot(maxEntriesPerRole))
            .ToArray();
    }

    public RoleTranscriptPage CreateTranscriptPage(string role, int beforeIndex, int maxEntries)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(beforeIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntries);
        if (!myRoles.TryGetValue(role, out var state))
            throw new InvalidOperationException($"Unknown role: {role}");
        return state.Transcript.CreateTranscriptPage(beforeIndex, maxEntries);
    }

    public RoleArchivedTranscriptEntry CreateArchivedTranscriptEntry(string role, int entryIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(entryIndex);
        if (!myRoles.TryGetValue(role, out var state))
            throw new InvalidOperationException($"Unknown role: {role}");
        return state.Transcript.CreateArchivedTranscriptEntry(entryIndex);
    }

    public AgentElicitationRequest GetPendingElicitation(string role, string requestId) =>
        myInteractions.GetElicitation(role, requestId);

    public bool? GetRoleReadiness(string role)
    {
        if (!myRoles.TryGetValue(role, out var state))
            return null;
        if (!myAdmission.IsAccepting)
            return false;
        if (myRoleOperations.IsInvalidated(role))
            return false;
        lock (state.SyncRoot)
            return state.Status == "idle" && !state.IsWorking;
    }

    public async Task<bool?> GetRoleReadinessAsync(
        string role,
        CancellationToken cancellationToken = default)
    {
        if (!myRoles.TryGetValue(role, out var state))
            return null;
        if (!myAdmission.IsAccepting)
            return false;
        if (myRoleOperations.IsInvalidated(role))
            return false;
        lock (state.SyncRoot)
        {
            if (state.Status is "error" or "stopped")
                return false;
        }
        if (mySessions.TryGetValue(role, out var session)
            && session is IAgentReadinessProbe readinessProbe)
        {
            var observation = await readinessProbe.ObserveReadinessAsync(cancellationToken);
            if (observation is not null)
            {
                try
                {
                    await EnqueueEventAsync(role, observation, cancellationToken);
                }
                catch (InvalidOperationException exception)
                    when (exception.Message == "Squad is shutting down")
                {
                    return false;
                }
            }
        }
        return GetRoleReadiness(role);
    }

    public void RegisterSession(IAgentSession session) => mySessions[session.Role] = session;

    /// <summary>
    /// Internal composition seam: lets <c>SquadApplication</c> wire this ViewModel to the shared lifecycle
    /// authority (<c>SessionRegistry</c>) so phase checks and session selection are atomic with the rest of the
    /// process, instead of this ViewModel tracking its own independent accepting flag. Safe to call more than once
    /// - a ViewModel reused across a fresh application simply adopts the new authority. Without a call to this
    /// method, the ViewModel falls back to a standalone admission decision so bare "ViewModel only" usage (as in
    /// unit-level specs) keeps working unchanged.
    /// </summary>
    public void UseAdmission(ISessionAdmission admission)
    {
        ArgumentNullException.ThrowIfNull(admission);
        myAdmission = admission;
    }

    public Task MarkRoleFailedAsync(string role, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return EnqueueCoreAsync(() =>
        {
            if (!myRoles.TryGetValue(role, out var state))
                return Task.CompletedTask;
            myRoleOperations.MarkRoleFailed(role);
            RemovePendingInteractionsForRole(role);
            lock (state.SyncRoot)
            {
                state.Status = "error";
                state.Error = exception.Message;
                state.IsWorking = false;
                state.ActiveTool = null;
            }
            NotifyStateChanged();
            return Task.CompletedTask;
        }, myShutdown.Token);
    }

    public void BeginStopping()
    {
        myStandaloneAdmission.BeginStopping();
        myCommands.Writer.TryComplete();
        myShutdown.Cancel();
    }

    public Task SendAsync(string role, string prompt, CancellationToken cancellationToken = default) =>
        TrackCommand(() => DispatchPromptAsync(role, (session, token) => session.SendAsync(prompt, token), cancellationToken));

    public Task SendHarnessAsync(string role, string prompt, CancellationToken cancellationToken = default) =>
        TrackCommand(() => DispatchPromptAsync(role, (session, token) => session.SendHarnessAsync(prompt, token), cancellationToken));

    public Task AbortAsync(string role, CancellationToken cancellationToken = default) =>
        AbortRoleAndWaitAsync(role, cancellationToken);

    public Task RequestPermissionAsync(AgentPermissionRequest request, CancellationToken cancellationToken = default) =>
        TrackCommand(() => EnqueueCoreAsync(() => ApplyEventAsync(request.Role, request), cancellationToken));

    public Task RequestInputAsync(AgentInputRequest request, CancellationToken cancellationToken = default) =>
        TrackCommand(() => EnqueueCoreAsync(() => ApplyEventAsync(request.Role, request), cancellationToken));

    public Task RequestElicitationAsync(AgentElicitationRequest request, CancellationToken cancellationToken = default) =>
        TrackCommand(() => EnqueueCoreAsync(() => ApplyEventAsync(request.Role, request), cancellationToken));

    public Task CompletePermissionAsync(string requestId, CancellationToken cancellationToken = default) =>
        TrackCommand(() => CompletePermissionCoreAsync(null, requestId, new AgentPermissionResponse(true), cancellationToken));

    public Task CompletePermissionAsync(string role, string requestId, bool approved, CancellationToken cancellationToken = default) =>
        TrackCommand(() => CompletePermissionCoreAsync(role, requestId, new AgentPermissionResponse(approved), cancellationToken));

    public Task CompleteInputAsync(string requestId, CancellationToken cancellationToken = default) =>
        TrackCommand(() => CompleteInputCoreAsync(null, requestId, new AgentInputResponse(null, true), cancellationToken));

    public Task CompleteInputAsync(string role, string requestId, string? answer, bool wasFreeform, CancellationToken cancellationToken = default) =>
        TrackCommand(() => CompleteInputCoreAsync(role, requestId, new AgentInputResponse(answer, wasFreeform), cancellationToken));

    public Task CompleteElicitationAsync(string requestId, CancellationToken cancellationToken = default) =>
        TrackCommand(() => CompleteElicitationCoreAsync(null, requestId, new AgentElicitationResponse("cancel", null), cancellationToken));

    public Task CompleteElicitationAsync(string role, string requestId, string action, JsonElement? content, CancellationToken cancellationToken = default) =>
        TrackCommand(() => CompleteElicitationCoreAsync(role, requestId, new AgentElicitationResponse(action, content), cancellationToken));

    public Task EnqueueEventAsync(string role, AgentEvent agentEvent, CancellationToken cancellationToken = default) =>
        TrackCommand(() => EnqueueCoreAsync(() => ApplyEventAsync(role, agentEvent), cancellationToken));

    public async Task StopAsync()
    {
        BeginStopping();
        await CancelAllPendingInteractionsAsync();
        Task[] accepted;
        lock (myAdmissionLock)
            accepted = myAcceptedCommands.ToArray();
        try
        {
            await Task.WhenAll(accepted);
        }
        catch (OperationCanceledException) when (myShutdown.IsCancellationRequested)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync();
        }
        finally
        {
            try
            {
                await myEventLoop;
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                myShutdown.Dispose();
                myRoleOperations.Dispose();
                myTranscriptArchive.Dispose();
            }
        }
    }

    private async Task EnqueueCoreAsync(Func<Task> command, CancellationToken cancellationToken)
    {
        EnsureAccepting();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var shutdownRegistration = myShutdown.Token.Register(() => completion.TrySetCanceled(myShutdown.Token));
        await myCommands.Writer.WriteAsync(async () =>
        {
            try
            {
                await command();
                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }, cancellationToken);
        await completion.Task;
    }

    private Task TrackCommand(Func<Task> operation)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (myAdmissionLock)
        {
            if (!myAdmission.IsAccepting)
                return Task.FromException(new InvalidOperationException("Squad is shutting down"));
            myAcceptedCommands.Add(completion.Task);
        }

        _ = CompleteTrackedCommandAsync(operation, completion);
        return completion.Task;
    }

    private async Task CompleteTrackedCommandAsync(Func<Task> operation, TaskCompletionSource completion)
    {
        try
        {
            await operation();
            completion.TrySetResult();
        }
        catch (OperationCanceledException exception)
        {
            completion.TrySetCanceled(exception.CancellationToken);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
        finally
        {
            lock (myAdmissionLock)
                myAcceptedCommands.Remove(completion.Task);
        }
    }

    private async Task RunEventLoopAsync()
    {
        await foreach (var command in myCommands.Reader.ReadAllAsync(myShutdown.Token))
            await command();
    }

    private Task ApplyEventAsync(string role, AgentEvent agentEvent)
    {
        if (!myRoles.TryGetValue(role, out var state))
            return Task.CompletedTask;
        if (myRoleOperations.IsRoleFailed(role))
            return Task.CompletedTask;
        if (agentEvent is AgentReadinessEvent readinessObservation
            && (!mySessions.TryGetValue(role, out var readinessSession)
                || readinessSession is not IAgentReadinessProbe readinessProbe
                || !readinessProbe.IsReadinessGenerationCurrent(readinessObservation.Generation)))
            return Task.CompletedTask;
        if (ShouldIgnoreEvent(role, agentEvent))
            return Task.CompletedTask;
        TranscriptUpdate? transcriptUpdate;
        lock (state.SyncRoot)
        {
            transcriptUpdate = myEventProjector.Project(state, agentEvent);
            if (transcriptUpdate is not null)
                TranscriptChanged?.Invoke(transcriptUpdate);
        }
        NotifyStateChanged(IsImmediateUiEvent(agentEvent));
        return Task.CompletedTask;
    }

    private async Task DispatchPromptAsync(string role, Func<IAgentSession, CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        using var lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, myShutdown.Token);
        using var promptLease = await myRoleOperations.AcquirePromptLeaseAsync(role, lifetimeCancellation.Token);
        EnsureRoleAvailable(role);
        await myRoleOperations.WaitForAbortAsync(role, lifetimeCancellation.Token);
        myRoleOperations.ResumeEvents(role);
        if (mySessions.TryGetValue(role, out var session)
            && session is IAgentReadinessProbe readinessProbe)
            readinessProbe.InvalidateReadiness();
        await EnqueueCoreAsync(() => MarkWaitingForResponseAsync(role), lifetimeCancellation.Token);
        await RunForRoleAsync(role, operation, lifetimeCancellation.Token);
    }

    private Task MarkWaitingForResponseAsync(string role)
    {
        if (myRoles.TryGetValue(role, out var state))
        {
            lock (state.SyncRoot)
            {
                state.IsWorking = true;
                state.ActiveTool = null;
            }
            NotifyStateChanged();
        }
        return Task.CompletedTask;
    }

    private async Task RunForRoleAsync(
        string role,
        Func<IAgentSession, CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        EnsureAccepting();
        using var lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, myShutdown.Token);
        if (!myAdmission.TryLeaseSession(role, out var lease))
            throw new InvalidOperationException($"Unknown role: {role}");
        using var operationLease = await myRoleOperations.AcquireOperationLeaseAsync(role, lifetimeCancellation.Token);
        EnsureAccepting();
        EnsureRoleAvailable(role);
        operationLease.Register(lifetimeCancellation);
        await operation(lease.Session, lifetimeCancellation.Token);
    }

    private void EnsureRoleAvailable(string role)
    {
        if (!myRoleOperations.IsRoleFailed(role))
            return;
        if (myRoles.TryGetValue(role, out var state))
        {
            lock (state.SyncRoot)
                throw new InvalidOperationException($"Role '{role}' is unavailable: {state.Error}");
        }
        throw new InvalidOperationException($"Role '{role}' is unavailable.");
    }

    private async Task AbortRoleAndWaitAsync(string role, CancellationToken cancellationToken)
    {
        var lease = myRoleOperations.TryBeginAbort(role, out var existingAbort);
        if (lease is null)
        {
            await existingAbort!.WaitAsync(cancellationToken);
            return;
        }

        using (lease)
        {
            try
            {
                await TrackCommand(() => AbortRoleAsync(role));
                lease.Complete();
            }
            catch (Exception exception)
            {
                lease.Fail(exception);
                throw;
            }
        }
    }

    private async Task AbortRoleAsync(string role)
    {
        try
        {
            await RunForRoleAsync(role, async (session, _) =>
            {
                await session.CancelPendingInteractionsAsync(CancellationToken.None);
                await session.AbortAsync(CancellationToken.None);
            }, CancellationToken.None);
        }
        finally
        {
            RemovePendingInteractionsForRole(role);
            MarkRoleIdle(role);
            NotifyStateChanged();
        }
    }

    private bool ShouldIgnoreEvent(string role, AgentEvent agentEvent)
    {
        if (agentEvent is AgentStartedEvent or AgentStoppedEvent or AgentSessionConfigurationEvent or AgentSessionModelChangedEvent or AgentContextUsageEvent or AgentSessionUsageEvent)
            return false;
        return myRoleOperations.IsInvalidated(role);
    }

    private void MarkRoleIdle(string role)
    {
        if (!myRoles.TryGetValue(role, out var state))
            return;
        lock (state.SyncRoot)
        {
            state.IsWorking = false;
            state.ActiveTool = null;
        }
    }

    private Task CompletePermissionCoreAsync(string? expectedRole, string requestId, AgentPermissionResponse response, CancellationToken cancellationToken) =>
        EnqueueCoreAsync(() => CompleteInteractionCoreAsync(
            myInteractions.RemovePermission, myInteractions.RegisterPermission, expectedRole, requestId,
            (session, token) => session.RespondToPermissionAsync(requestId, response, token), cancellationToken), cancellationToken);

    private Task CompleteInputCoreAsync(string? expectedRole, string requestId, AgentInputResponse response, CancellationToken cancellationToken) =>
        EnqueueCoreAsync(() => CompleteInteractionCoreAsync(
            myInteractions.RemoveInput, myInteractions.RegisterInput, expectedRole, requestId,
            (session, token) => session.RespondToInputAsync(requestId, response, token), cancellationToken), cancellationToken);

    private Task CompleteElicitationCoreAsync(string? expectedRole, string requestId, AgentElicitationResponse response, CancellationToken cancellationToken) =>
        EnqueueCoreAsync(() => CompleteInteractionCoreAsync(
            myInteractions.RemoveElicitation, myInteractions.RegisterElicitation, expectedRole, requestId,
            (session, token) => session.RespondToElicitationAsync(requestId, response, token), cancellationToken), cancellationToken);

    private async Task CompleteInteractionCoreAsync<TRequest>(
        Func<string?, string, (string Role, TRequest Request)> remove,
        Action<TRequest> restore,
        string? expectedRole,
        string requestId,
        Func<IAgentSession, CancellationToken, Task> respond,
        CancellationToken cancellationToken)
    {
        var (role, request) = remove(expectedRole, requestId);
        try
        {
            await RunForRoleAsync(role, respond, cancellationToken);
            UnprotectPendingTranscriptEntry(role, requestId);
            NotifyStateChanged();
        }
        catch
        {
            if (!myRoleOperations.IsRoleFailed(role))
                restore(request);
            else
                UnprotectPendingTranscriptEntry(role, requestId);
            NotifyStateChanged();
            throw;
        }
    }

    private async Task CancelAllPendingInteractionsAsync()
    {
        var sessions = mySessions.Values.ToArray();
        foreach (var session in sessions)
        {
            if (!session.Completion.IsCompleted)
                await session.CancelPendingInteractionsAsync();
        }
        myInteractions.Clear();
        NotifyStateChanged();
    }

    private void RemovePendingInteractionsForRole(string role)
    {
        foreach (var protectedEntry in myInteractions.RemoveForRole(role))
            if (myRoles.TryGetValue(protectedEntry.Role, out var state))
                lock (state.SyncRoot)
                    state.Transcript.UnprotectTranscriptEntry(protectedEntry.EntryIndex);
    }

    private void UnprotectPendingTranscriptEntry(string role, string requestId)
    {
        var protectedEntry = myInteractions.TryRemoveProtectedTranscriptEntry(role, requestId);
        if (protectedEntry is null)
            return;
        if (myRoles.TryGetValue(protectedEntry.Value.Role, out var state))
            lock (state.SyncRoot)
                state.Transcript.UnprotectTranscriptEntry(protectedEntry.Value.EntryIndex);
    }

    private static void ValidateTranscriptRetentionOptions(TranscriptRetentionOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxRetainedEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxRetainedContentCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxRetainedEntryCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxArchivedEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxArchivedContentCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxArchivedEntryCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxAnnouncementCharacters);
        if (options.MaxRetainedEntries < 2)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "At least two retained entries are required for concurrent assistant and reasoning streams.");
        if (options.MaxRetainedContentCharacters < 2)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "At least two retained content characters are required for concurrent assistant and reasoning streams.");
        if (options.MaxRetainedEntryCharacters > options.MaxRetainedContentCharacters)
            throw new ArgumentException(
                "The retained entry limit cannot exceed the retained content limit.",
                nameof(options));
        if (options.MaxArchivedEntryCharacters > options.MaxArchivedContentCharacters)
            throw new ArgumentException(
                "The archived entry limit cannot exceed the archived content limit.",
                nameof(options));
        if (options.MaxArchivedEntryCharacters <= options.MaxRetainedEntryCharacters)
            throw new ArgumentException(
                "The archived entry limit must exceed the retained entry limit.",
                nameof(options));
    }

    private void EnsureAccepting()
    {
        if (!myAdmission.IsAccepting)
            throw new InvalidOperationException("Squad is shutting down");
    }

    private void NotifyStateChanged(bool immediate = true)
    {
        foreach (Action listener in StateChanged?.GetInvocationList() ?? [])
        {
            try
            {
                listener();
            }
            catch
            {
            }
        }
        foreach (Action<UiRefreshPriority> listener in SnapshotRequested?.GetInvocationList() ?? [])
        {
            try
            {
                listener(immediate ? UiRefreshPriority.Immediate : UiRefreshPriority.Deferred);
            }
            catch
            {
            }
        }
    }

    private static bool IsImmediateUiEvent(AgentEvent agentEvent) =>
        agentEvent is AgentErrorEvent or AgentEventError or AgentReadinessEvent or AgentIdleEvent or AgentStoppedEvent
            or AgentPermissionRequest or AgentInputRequest or AgentElicitationRequest;

    /// <summary>
    /// The default <see cref="ISessionAdmission"/> used until <see cref="UseAdmission"/> injects an external
    /// lifecycle authority (e.g. headquarters' SessionRegistry). Replicates the ViewModel's own former
    /// "accepting" flag against its own session dictionary, so tests that construct a bare <see cref="SquadViewModel"/>
    /// with no owning application - calling <see cref="RegisterSession"/> / <see cref="BeginStopping"/> directly -
    /// keep their current admission and lease semantics unchanged.
    /// </summary>
    private sealed class StandaloneSessionAdmission(SquadViewModel owner) : ISessionAdmission
    {
        private readonly object myLock = new();
        private bool myAccepting = true;

        public bool IsAccepting
        {
            get { lock (myLock) return myAccepting; }
        }

        public void BeginStopping()
        {
            lock (myLock)
                myAccepting = false;
        }

        public bool TryLeaseSession(string role, out SessionLease lease)
        {
            lock (myLock)
            {
                if (myAccepting
                    && owner.mySessions.TryGetValue(role, out var session)
                    && !session.Completion.IsCompleted)
                {
                    lease = new SessionLease(0, session);
                    return true;
                }
            }
            lease = default;
            return false;
        }
    }
}



