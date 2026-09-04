using global::squad.AgentProvider.Abstractions;
using global::squad.AgentProvider.Abstractions.Agents;
using global::squad.Core.Interactions;
using global::squad.Core.Transcripts;
using global::squad.Ui.Abstractions;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Threading.Channels;

namespace squad.Core;

public sealed class SquadViewModel : ISquadUi, ITranscriptUi, IAsyncDisposable
{
    private readonly Channel<Func<Task>> myCommands = Channel.CreateUnbounded<Func<Task>>();
    private readonly CancellationTokenSource myShutdown = new();
    private readonly ConcurrentDictionary<string, IAgentSession> mySessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AgentRoleState> myRoles = new(StringComparer.Ordinal);
    private readonly TranscriptArchive myTranscriptArchive;
    private readonly TranscriptRetentionOptions myTranscriptRetentionOptions;
    private readonly Dictionary<string, SemaphoreSlim> myRoleLocks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SemaphoreSlim> myPromptLocks = new(StringComparer.Ordinal);
    private readonly object myRoleOperationsLock = new();
    private readonly Dictionary<string, CancellationTokenSource> myActiveRoleOperations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TaskCompletionSource> myRoleAborts = new(StringComparer.Ordinal);
    private readonly HashSet<string> myInvalidatedRoles = new(StringComparer.Ordinal);
    private readonly HashSet<string> myFailedRoleAborts = new(StringComparer.Ordinal);
    private readonly HashSet<string> myFailedRoles = new(StringComparer.Ordinal);
    private readonly Task myEventLoop;
    private readonly object myAdmissionLock = new();
    private readonly HashSet<Task> myAcceptedCommands = [];
    private bool myAccepting = true;

    public SquadViewModel()
        : this(new TranscriptRetentionOptions())
    {
    }

    public SquadViewModel(TranscriptRetentionOptions transcriptRetentionOptions)
    {
        ValidateTranscriptRetentionOptions(transcriptRetentionOptions);
        myTranscriptRetentionOptions = transcriptRetentionOptions;
        myTranscriptArchive = new TranscriptArchive(transcriptRetentionOptions);
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

    private readonly PendingInteractionRegistry myInteractions = new();

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
        lock (myAdmissionLock)
        {
            if (!myAccepting)
                return false;
            lock (myRoleOperationsLock)
            {
                if (myInvalidatedRoles.Contains(role))
                    return false;
            }
            lock (state.SyncRoot)
                return state.Status == "idle" && !state.IsWorking;
        }
    }

    public async Task<bool?> GetRoleReadinessAsync(
        string role,
        CancellationToken cancellationToken = default)
    {
        if (!myRoles.TryGetValue(role, out var state))
            return null;
        lock (myAdmissionLock)
        {
            if (!myAccepting)
                return false;
        }
        lock (myRoleOperationsLock)
        {
            if (myInvalidatedRoles.Contains(role))
                return false;
        }
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

    public Task MarkRoleFailedAsync(string role, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return EnqueueCoreAsync(() =>
        {
            if (!myRoles.TryGetValue(role, out var state))
                return Task.CompletedTask;
            lock (myRoleOperationsLock)
            {
                myFailedRoles.Add(role);
                CancelRoleOperation(role);
            }
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
        lock (myAdmissionLock)
        {
            if (!myAccepting)
                return;
            myAccepting = false;
            myCommands.Writer.TryComplete();
            myShutdown.Cancel();
        }
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
                foreach (var roleLock in myRoleLocks.Values)
                    roleLock.Dispose();
                foreach (var promptLock in myPromptLocks.Values)
                    promptLock.Dispose();
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
            if (!myAccepting)
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
        if (IsRoleFailed(role))
            return Task.CompletedTask;
        if (agentEvent is AgentReadinessEvent readinessObservation
            && (!mySessions.TryGetValue(role, out var readinessSession)
                || readinessSession is not IAgentReadinessProbe readinessProbe
                || !readinessProbe.IsReadinessGenerationCurrent(readinessObservation.Generation)))
            return Task.CompletedTask;
        if (ShouldIgnoreEvent(role, agentEvent))
            return Task.CompletedTask;
        TranscriptUpdate? transcriptUpdate = null;
        lock (state.SyncRoot)
        {
            state.EventCount++;
            state.LastEventAt = agentEvent.OccurredAt;
            switch (agentEvent)
            {
                case AgentStartedEvent:
                state.Status = "running";
                state.IsWorking = false;
                transcriptUpdate = AddTranscriptEntry(state, agentEvent.OccurredAt, "harness", "Session started.");
                    break;
                case AgentStoppedEvent:
                state.Status = "stopped";
                state.IsWorking = false;
                transcriptUpdate = AddTranscriptEntry(state, agentEvent.OccurredAt, "harness", "Session stopped.");
                    break;
                case AgentErrorEvent error:
                state.Status = "error";
                state.IsWorking = false;
                state.Error = error.Message;
                transcriptUpdate = AddTranscriptEntry(state, agentEvent.OccurredAt, "error", error.Message);
                    break;
                case AgentEventError error:
                state.Status = "error";
                state.IsWorking = false;
                state.Error = error.Message;
                transcriptUpdate = AddTranscriptEntry(state, agentEvent.OccurredAt, "error", error.Message);
                    break;
                case AgentIdleEvent:
                state.Status = "idle";
                state.IsWorking = false;
                state.Transcript.FinalizeAssistantEntry();
                state.Transcript.FinalizeReasoningEntry();
                break;
                case AgentReadinessEvent readiness:
                    state.Status = readiness.State;
                    state.IsWorking = readiness.State == "busy";
                    if (readiness.State == "error")
                        state.Error = readiness.Error;
                    break;
                case AgentUserMessageEvent message:
                state.IsWorking = true;
                state.Transcript.FinalizeAssistantEntry();
                state.Transcript.FinalizeReasoningEntry();
                transcriptUpdate = AddTranscriptEntry(state, message.OccurredAt, "user", message.Content);
                    break;
                case AgentHarnessMessageEvent message:
                transcriptUpdate = AddTranscriptEntry(state, message.OccurredAt, "harness", message.Content);
                    break;
                case AgentReasoningEvent reasoning:
                state.IsWorking = true;
                transcriptUpdate = ApplyReasoning(state, reasoning);
                    break;
                case AgentAssistantMessageEvent message:
                state.IsWorking = true;
                transcriptUpdate = ApplyAssistantMessage(state, message);
                    break;
                case AgentSubagentStartedEvent subagent:
                    state.IsWorking = true;
                    transcriptUpdate = AddTranscriptEntry(
                        state,
                        subagent.OccurredAt,
                        "subagent",
                        DescribeSubagent(subagent));
                    break;
                case AgentSkillInvokedEvent skill:
                    state.IsWorking = true;
                    transcriptUpdate = AddTranscriptEntry(
                        state,
                        skill.OccurredAt,
                        "tool",
                        $"using skill({skill.Name})");
                    break;
                case AgentToolStartedEvent tool:
                    state.IsWorking = true;
                    if (IsSubagentPlumbingTool(tool.ToolName) ||
                        tool.ToolName.Equals("skill", StringComparison.OrdinalIgnoreCase))
                        break;
                    var isRead = tool.Kind == "read" || IsReadTool(tool.ToolName);
                    var suppressOutput = isRead;
                    var toolDescription = isRead ? DescribeRead(tool) : DescribeToolStart(tool);
                    if (!string.IsNullOrWhiteSpace(toolDescription))
                    {
                        transcriptUpdate = state.Transcript.StartTool(
                            tool.ToolCallId,
                            tool.ToolName,
                            suppressOutput,
                            isRead && !HasReadRange(tool),
                            new TranscriptEntry(
                                tool.OccurredAt,
                                isRead ? "read" : "tool",
                                toolDescription));
                        state.ActiveTool = tool.ToolName;
                    }
                    break;
                case AgentToolCompletedEvent tool:
                    var completion = state.Transcript.CompleteTool(
                        tool.ToolCallId,
                        tool.DisplayOutputFallback,
                        tool.ContentFallback);
                    if (completion is not null)
                    {
                        transcriptUpdate = completion.Update;
                        state.ActiveTool = completion.ActiveTool;
                    }
                    break;
                case AgentToolOutputChangedEvent output:
                    state.IsWorking = true;
                    transcriptUpdate = state.Transcript.ChangeToolOutput(
                        output.ToolCallId,
                        output.Output);
                    break;
                case AgentToolProgressEvent progress:
                    state.IsWorking = true;
                    transcriptUpdate = state.Transcript.ChangeToolProgress(
                        progress.ToolCallId,
                        progress.Progress);
                    break;
                case AgentSessionConfigurationEvent configuration:
                    state.Model = configuration.Model;
                    state.Effort = configuration.Effort;
                    if (state.ContextLimitTokens is null or <= 0)
                        state.ContextLimitTokens = GetModelContextWindowLimit(state.Model, 0);
                    break;
                case AgentSessionModelChangedEvent model:
                    state.Model = model.Model;
                    state.Effort = model.Effort;
                    state.ContextLimitTokens = GetModelContextWindowLimit(state.Model, state.ContextLimitTokens ?? 0);
                    break;
                case AgentSessionUsageEvent usage:
                    state.AicUsed = Math.Max(state.AicUsed ?? 0, usage.AicUsed);
                    break;
                case AgentContextUsageEvent usage:
                    state.ContextUsedTokens = usage.UsedTokens;
                    state.ContextLimitTokens = GetModelContextWindowLimit(state.Model, usage.LimitTokens);
                    break;
                case AgentSystemMessageEvent message:
                transcriptUpdate = AddTranscriptEntry(state, message.OccurredAt, "system", message.Content);
                    break;
                case AgentPermissionRequest permission:
                myInteractions.RegisterPermission(permission);
                transcriptUpdate = AddTranscriptEntry(state, permission.OccurredAt, "harness", $"Permission required: {permission.Description}.", protect: true);
                myInteractions.ProtectTranscriptEntry(permission.Role, permission.RequestId, transcriptUpdate.EntryIndex);
                    break;
                case AgentInputRequest input:
                myInteractions.RegisterInput(input);
                transcriptUpdate = AddTranscriptEntry(state, input.OccurredAt, "harness", input.Prompt, protect: true);
                myInteractions.ProtectTranscriptEntry(input.Role, input.RequestId, transcriptUpdate.EntryIndex);
                    break;
                case AgentElicitationRequest elicitation:
                myInteractions.RegisterElicitation(elicitation);
                transcriptUpdate = AddTranscriptEntry(state, elicitation.OccurredAt, "harness", elicitation.Prompt, protect: true);
                myInteractions.ProtectTranscriptEntry(elicitation.Role, elicitation.RequestId, transcriptUpdate.EntryIndex);
                    break;
                }
            if (transcriptUpdate is not null)
                TranscriptChanged?.Invoke(transcriptUpdate);
        }
            NotifyStateChanged(IsImmediateUiEvent(agentEvent));
        return Task.CompletedTask;
    }

    private static TranscriptUpdate ApplyAssistantMessage(AgentRoleState state, AgentAssistantMessageEvent message)
    {
        if (message.IsDelta)
            return state.Transcript.AppendAssistantEntry(message.OccurredAt, message.Content);

        return state.Transcript.CompleteAssistantEntry(message.OccurredAt, message.Content);
    }

    private static TranscriptUpdate ApplyReasoning(AgentRoleState state, AgentReasoningEvent reasoning)
    {
        if (reasoning.IsDelta)
            return state.Transcript.AppendReasoningEntry(reasoning.OccurredAt, reasoning.Content);

        return state.Transcript.CompleteReasoningEntry(reasoning.OccurredAt, reasoning.Content);
    }

    private static readonly HashSet<string> KnownShellRunners = new(StringComparer.OrdinalIgnoreCase)
    {
        "powershell", "pwsh", "bash", "sh", "zsh", "cmd", "run_in_terminal", "execute", "shell"
    };

    private static readonly HashSet<string> KnownReadTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "view", "read_file", "view_file"
    };

    private static readonly HashSet<string> SubagentPlumbingTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "task", "read_agent", "list_agents"
    };

    private static string DescribeSubagent(AgentSubagentStartedEvent subagent)
    {
        var agentName = string.IsNullOrWhiteSpace(subagent.AgentName)
            ? null
            : HumanizeIdentifier(subagent.AgentName);
        var displayName = string.IsNullOrWhiteSpace(subagent.AgentDisplayName)
            ? null
            : subagent.AgentDisplayName.Trim();
        var label = agentName ?? displayName ?? "Subagent";
        var taskDescription = agentName is not null && displayName is not null &&
            !AreEquivalentLabels(subagent.AgentName!, displayName)
                ? displayName
                : null;

        return string.Join(" · ", new[] { label, subagent.Model?.Trim(), taskDescription }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string HumanizeIdentifier(string value) =>
        string.Join(' ', value.Trim()
            .Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private static bool AreEquivalentLabels(string left, string right) =>
        string.Equals(NormalizeLabel(left), NormalizeLabel(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeLabel(string value) =>
        new(value.Where(char.IsLetterOrDigit).ToArray());

    private static string? DescribeToolStart(AgentToolStartedEvent tool)
    {
        if (string.IsNullOrWhiteSpace(tool.Arguments))
            return tool.ToolName;

        if (TryParseToolArguments(tool.Arguments, out var arguments))
        {
            if (arguments.ValueKind is JsonValueKind.Object &&
                arguments.TryGetProperty("command", out var command) &&
                command.ValueKind is JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(command.GetString()))
            {
                var cmd = command.GetString()!;
                if (tool.ToolName.Contains(cmd, StringComparison.Ordinal))
                    return tool.ToolName;
                if (KnownShellRunners.Contains(tool.ToolName))
                    return $"{tool.ToolName} {cmd}";
                return cmd;
            }

            if (arguments.ValueKind is JsonValueKind.Object &&
                (arguments.TryGetProperty("path", out var pathProp) ||
                 arguments.TryGetProperty("filePath", out pathProp) ||
                 arguments.TryGetProperty("file_path", out pathProp)) &&
                pathProp.ValueKind is JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(pathProp.GetString()))
            {
                var path = pathProp.GetString()!;
                var fileName = Path.GetFileName(path);
                if (tool.ToolName.Contains(path, StringComparison.Ordinal) ||
                    (!string.IsNullOrEmpty(fileName) && tool.ToolName.Contains(fileName, StringComparison.Ordinal)))
                {
                    return tool.ToolName;
                }

                return $"{tool.ToolName} {path}";
            }

            return tool.ToolName.Contains(' ', StringComparison.Ordinal)
                ? tool.ToolName
                : $"{tool.ToolName} {arguments.GetRawText()}";
        }

        return tool.ToolName;
    }

    private static bool IsReadTool(string toolName) => KnownReadTools.Contains(toolName);

    private static bool IsSubagentPlumbingTool(string toolName) => SubagentPlumbingTools.Contains(toolName);

    private static string DescribeRead(AgentToolStartedEvent tool)
    {
        if (string.IsNullOrWhiteSpace(tool.Arguments))
            return tool.ToolName;
        if (!TryParseToolArguments(tool.Arguments, out var arguments))
            return tool.ToolName;

        var path = ReadPath(arguments);
        if (string.IsNullOrWhiteSpace(path))
            return tool.ToolName;
        var fullPath = Path.IsPathFullyQualified(path) || string.IsNullOrWhiteSpace(tool.WorkingDirectory)
            ? path
            : Path.GetFullPath(path, tool.WorkingDirectory);
        return TryReadRange(arguments, out var startLine, out var endLine)
            ? $"{fullPath} [{startLine}..{endLine}]"
            : fullPath;
    }

    private static bool HasReadRange(AgentToolStartedEvent tool) =>
        TryParseToolArguments(tool.Arguments, out var arguments) &&
        TryReadRange(arguments, out _, out _);

    private static bool TryReadRange(JsonElement arguments, out int startLine, out int endLine)
    {
        startLine = 0;
        endLine = 0;
        if (arguments.ValueKind is not JsonValueKind.Object)
            return false;
        if (arguments.TryGetProperty("view_range", out var range) &&
            range.ValueKind is JsonValueKind.Array &&
            range.GetArrayLength() == 2 &&
            range[0].TryGetInt32(out startLine) &&
            range[1].TryGetInt32(out endLine))
        {
            return true;
        }

        return TryReadLine(arguments, ["startLine", "start_line"], out startLine) &&
            TryReadLine(arguments, ["endLine", "end_line"], out endLine);
    }

    private static bool TryReadLine(JsonElement arguments, string[] names, out int line)
    {
        foreach (var name in names)
            if (arguments.TryGetProperty(name, out var value) && value.TryGetInt32(out line))
                return true;
        line = 0;
        return false;
    }

    private static string? ReadPath(JsonElement arguments)
    {
        if (arguments.ValueKind is not JsonValueKind.Object)
            return null;
        foreach (var propertyName in new[] { "path", "filePath", "file_path", "filename" })
            if (arguments.TryGetProperty(propertyName, out var property) && property.ValueKind is JsonValueKind.String)
                return property.GetString();
        return null;
    }

    private static bool TryParseToolArguments(string? arguments, out JsonElement value)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            value = default;
            return false;
        }
        try
        {
            using var document = JsonDocument.Parse(arguments);
            value = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            value = default;
            return false;
        }
    }

    private static TranscriptUpdate AddTranscriptEntry(
        AgentRoleState state,
        DateTimeOffset occurredAt,
        string source,
        string content,
        bool protect = false) =>
        state.Transcript.AddTranscriptEntry(new TranscriptEntry(occurredAt, source, content), protect);

    private async Task DispatchPromptAsync(string role, Func<IAgentSession, CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        using var lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, myShutdown.Token);
        var promptLock = GetPromptLock(role);
        await promptLock.WaitAsync(lifetimeCancellation.Token);
        try
        {
            EnsureRoleAvailable(role);
            await WaitForAbortAsync(role, lifetimeCancellation.Token);
            ResumeRoleEvents(role);
            if (mySessions.TryGetValue(role, out var session)
                && session is IAgentReadinessProbe readinessProbe)
                readinessProbe.InvalidateReadiness();
            await EnqueueCoreAsync(() => MarkWaitingForResponseAsync(role), lifetimeCancellation.Token);
            await RunForRoleAsync(role, operation, lifetimeCancellation.Token);
        }
        finally
        {
            promptLock.Release();
        }
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
        if (!mySessions.TryGetValue(role, out var session))
            throw new InvalidOperationException($"Unknown role: {role}");
        var roleLock = GetRoleLock(role);
        await roleLock.WaitAsync(lifetimeCancellation.Token);
        try
        {
            EnsureAccepting();
            EnsureRoleAvailable(role);
            RegisterRoleOperation(role, lifetimeCancellation);
            await operation(session, lifetimeCancellation.Token);
        }
        finally
        {
            UnregisterRoleOperation(role, lifetimeCancellation);
            roleLock.Release();
        }
    }

    private void EnsureRoleAvailable(string role)
    {
        if (!IsRoleFailed(role))
            return;
        if (myRoles.TryGetValue(role, out var state))
        {
            lock (state.SyncRoot)
                throw new InvalidOperationException($"Role '{role}' is unavailable: {state.Error}");
        }
        throw new InvalidOperationException($"Role '{role}' is unavailable.");
    }

    private bool IsRoleFailed(string role)
    {
        lock (myRoleOperationsLock)
            return myFailedRoles.Contains(role);
    }

    private async Task AbortRoleAndWaitAsync(string role, CancellationToken cancellationToken)
    {
        TaskCompletionSource? completion = null;
        Task? existingAbort = null;
        lock (myRoleOperationsLock)
        {
            if (myRoleAborts.TryGetValue(role, out var existing))
            {
                existingAbort = existing.Task;
            }
            else
            {
                completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                myRoleAborts.Add(role, completion);
                myInvalidatedRoles.Add(role);
                CancelRoleOperation(role);
            }
        }

        if (existingAbort is not null)
        {
            await existingAbort.WaitAsync(cancellationToken);
            return;
        }

        try
        {
            await TrackCommand(() => AbortRoleAsync(role));
            lock (myRoleOperationsLock)
                myFailedRoleAborts.Remove(role);
            completion!.TrySetResult();
        }
        catch (Exception exception)
        {
            lock (myRoleOperationsLock)
                myFailedRoleAborts.Add(role);
            completion!.TrySetException(exception);
            throw;
        }
        finally
        {
            lock (myRoleOperationsLock)
                myRoleAborts.Remove(role);
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

    private Task WaitForAbortAsync(string role, CancellationToken cancellationToken)
    {
        Task? abort;
        var abortFailed = false;
        lock (myRoleOperationsLock)
        {
            abort = myRoleAborts.GetValueOrDefault(role)?.Task;
            abortFailed = myFailedRoleAborts.Contains(role);
        }
        return abort?.WaitAsync(cancellationToken) ??
            (abortFailed
                ? Task.FromException(new InvalidOperationException($"Role '{role}' remains cancelled because its abort failed."))
                : Task.CompletedTask);
    }

    private void ResumeRoleEvents(string role)
    {
        lock (myRoleOperationsLock)
            myInvalidatedRoles.Remove(role);
    }

    private void RegisterRoleOperation(string role, CancellationTokenSource cancellation)
    {
        lock (myRoleOperationsLock)
        {
            myActiveRoleOperations[role] = cancellation;
            if (myInvalidatedRoles.Contains(role))
                cancellation.Cancel();
        }
    }

    private void UnregisterRoleOperation(string role, CancellationTokenSource cancellation)
    {
        lock (myRoleOperationsLock)
            if (myActiveRoleOperations.TryGetValue(role, out var active) && ReferenceEquals(active, cancellation))
                myActiveRoleOperations.Remove(role);
    }

    private void CancelRoleOperation(string role)
    {
        if (myActiveRoleOperations.TryGetValue(role, out var operation))
            operation.Cancel();
    }

    private bool ShouldIgnoreEvent(string role, AgentEvent agentEvent)
    {
        if (agentEvent is AgentStartedEvent or AgentStoppedEvent or AgentSessionConfigurationEvent or AgentSessionModelChangedEvent or AgentContextUsageEvent or AgentSessionUsageEvent)
            return false;
        lock (myRoleOperationsLock)
            return myInvalidatedRoles.Contains(role);
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
            if (!IsRoleFailed(role))
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

    private static long GetModelContextWindowLimit(string? model, long reportedLimit)
    {
        var knownLimit = GetKnownModelLimit(model);
        if (knownLimit > 0)
            return Math.Max(knownLimit, reportedLimit);
        return reportedLimit > 0 ? reportedLimit : 128000;
    }

    private static long GetKnownModelLimit(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return 0;

        var lower = model.ToLowerInvariant();
        if (lower.Contains("claude"))
            return 200000;
        if (lower.Contains("gpt-4o") || lower.Contains("gpt-4.5") || lower.Contains("o1") || lower.Contains("o3"))
            return 128000;

        return 128000;
    }

    private SemaphoreSlim GetRoleLock(string role)
    {
        lock (myRoleLocks)
        {
            if (!myRoleLocks.TryGetValue(role, out var roleLock))
                myRoleLocks[role] = roleLock = new SemaphoreSlim(1, 1);
            return roleLock;
        }
    }

    private SemaphoreSlim GetPromptLock(string role)
    {
        lock (myPromptLocks)
        {
            if (!myPromptLocks.TryGetValue(role, out var promptLock))
                myPromptLocks[role] = promptLock = new SemaphoreSlim(1, 1);
            return promptLock;
        }
    }

    private void EnsureAccepting()
    {
        lock (myAdmissionLock)
        {
            if (!myAccepting)
                throw new InvalidOperationException("Squad is shutting down");
        }
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
}



