using global::squad.AgentProvider.Abstractions.Agents;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using System.Collections;
using System.Diagnostics;
using System.Text.Json;

#pragma warning disable GHCP001

namespace squad.CopilotSdk;

internal sealed class CopilotSdkClient : IAsyncDisposable
{
    private readonly CopilotClient myClient;

    public CopilotSdkClient(CopilotClient client)
    {
        myClient = client;
    }

    public static async Task<CopilotSdkClient> StartAsync(string workingDirectory, IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken = default)
    {
        var connection = (ChildProcessRuntimeConnection)RuntimeConnection.ForStdio();
        connection.Environment = environment;
        var client = new CopilotClient(new CopilotClientOptions
        {
            Connection = connection,
            WorkingDirectory = workingDirectory,
        });
        await client.StartAsync(cancellationToken);
        return new CopilotSdkClient(client);
    }

    public async Task<CopilotSdkRuntimeSession> CreateSessionAsync(string workingDirectory, CopilotSdkAgentSession agentSession, string permissions, string? model, string? effort, CancellationToken cancellationToken = default)
    {
        var toolEvents = new CopilotToolEventNormalizer(agentSession, workingDirectory);
        var session = await myClient.CreateSessionAsync(new SessionConfig
        {
            WorkingDirectory = workingDirectory,
            EnableConfigDiscovery = true,
            Model = model,
            ReasoningEffort = effort,
            OnEvent = sessionEvent =>
            {
                RawSdkEventTrace.Record(sessionEvent);
                if (!toolEvents.TryPublish(sessionEvent))
                    PublishEvent(sessionEvent, agentSession);
            },
            OnPermissionRequest = (request, _) => HandlePermissionRequestAsync(agentSession, permissions, workingDirectory, request),
            OnUserInputRequest = (request, _) => HandleUserInputRequestAsync(agentSession, request),
            OnElicitationRequest = request => HandleElicitationRequestAsync(agentSession, request),
        }, cancellationToken);
        return new CopilotSdkRuntimeSession(session);
    }

    public Task StopAsync(CancellationToken cancellationToken = default) => myClient.StopAsync();

    public Task ForceStopAsync(CancellationToken cancellationToken = default) => myClient.ForceStopAsync();

    public ValueTask DisposeAsync() => myClient.DisposeAsync();

    private static async Task<PermissionDecision> HandlePermissionRequestAsync(CopilotSdkAgentSession agentSession, string permissions, string workingDirectory, PermissionRequest request)
    {
        if (permissions == "approveAll" && request.ManagedApprovalRequired is not true)
            return PermissionDecision.ApproveOnce();
        if (ShouldApproveWorkspaceRead(workingDirectory, request))
            return PermissionDecision.ApproveOnce();

        var response = await agentSession.RequestPermissionAsync(request.Kind.ToString());
        return response.Approved
            ? PermissionDecision.ApproveOnce()
            : PermissionDecision.Reject("Permission rejected by user.");
    }

    private static bool ShouldApproveWorkspaceRead(string workingDirectory, PermissionRequest request)
    {
        if (request.ManagedApprovalRequired is true || request is not PermissionRequestRead { Path: { Length: > 0 } path })
            return false;

        try
        {
            var normalizedWorkingDirectory = Path.GetFullPath(workingDirectory);
            var normalizedPath = Path.GetFullPath(Path.IsPathFullyQualified(path) ? path : Path.Combine(normalizedWorkingDirectory, path));
            var relativePath = Path.GetRelativePath(normalizedWorkingDirectory, normalizedPath);
            return relativePath != ".." &&
                !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal) &&
                !Path.IsPathFullyQualified(relativePath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static async Task<UserInputResponse> HandleUserInputRequestAsync(CopilotSdkAgentSession agentSession, UserInputRequest request)
    {
        var response = await agentSession.RequestInputAsync(request.Question, request.Choices?.ToArray(), request.AllowFreeform ?? true);
        return new UserInputResponse
        {
            Answer = response.Answer ?? string.Empty,
            WasFreeform = response.WasFreeform,
        };
    }

    private static async Task<ElicitationResult> HandleElicitationRequestAsync(CopilotSdkAgentSession agentSession, ElicitationContext request)
    {
        JsonElement? schema = request.RequestedSchema is null ? null : JsonSerializer.SerializeToElement(request.RequestedSchema);
        var response = await agentSession.RequestElicitationAsync(request.Message, request.Mode?.ToString() ?? "form", schema, request.Url);
        return new ElicitationResult
        {
            Action = new UIElicitationResponseAction(response.Action),
            Content = ToSdkElicitationContent(response.Content),
        };
    }

    private static IDictionary<string, object>? ToSdkElicitationContent(JsonElement? content)
    {
        if (content is not { ValueKind: JsonValueKind.Object } value)
            return null;
        return JsonSerializer.Deserialize<Dictionary<string, object>>(value.GetRawText());
    }

    private static void PublishEvent(SessionEvent sessionEvent, CopilotSdkAgentSession agentSession)
    {
        var occurredAt = DateTimeOffset.UtcNow;
        switch (sessionEvent)
        {
            case UserMessageEvent { Data.Content: { } content }:
                if (!agentSession.TryConsumeHarnessMessageEcho(content))
                    agentSession.Publish(new AgentUserMessageEvent(occurredAt, content));
                break;
            case AssistantMessageDeltaEvent { Data.DeltaContent: { } content }:
                agentSession.Publish(new AgentAssistantMessageEvent(occurredAt, content, true));
                break;
            case AssistantMessageEvent { Data.Content: { } content }:
                agentSession.Publish(new AgentAssistantMessageEvent(occurredAt, content, false));
                break;
            case AssistantReasoningDeltaEvent { Data.DeltaContent: { } content }:
                agentSession.Publish(new AgentReasoningEvent(occurredAt, content, true));
                break;
            case AssistantReasoningEvent { Data.Content: { } content }:
                agentSession.Publish(new AgentReasoningEvent(occurredAt, content, false));
                break;
            case SessionStartEvent:
                agentSession.Publish(new AgentStartedEvent(occurredAt));
                break;
            case SessionModelChangeEvent { Data: { } data }:
                agentSession.Publish(new AgentSessionModelChangedEvent(occurredAt, data.NewModel?.ToString(), data.ReasoningEffort?.ToString()));
                break;
            case SessionUsageCheckpointEvent { Data: { } data }:
                agentSession.Publish(new AgentSessionUsageEvent(occurredAt, Convert.ToDecimal((object?)data.TotalNanoAiu) / 1_000_000_000m));
                break;
            case SessionSkillsLoadedEvent:
                PublishDiscoveredSkills(sessionEvent, agentSession, occurredAt);
                break;
            case SkillInvokedEvent:
                PublishSkillInvocation(sessionEvent, agentSession, occurredAt);
                break;
            case SubagentStartedEvent { Data: { } data }:
                agentSession.Publish(new AgentSubagentStartedEvent(
                    occurredAt,
                    data.AgentName,
                    data.AgentDisplayName,
                    data.Model?.ToString()));
                break;
            case SessionIdleEvent:
                agentSession.Publish(new AgentIdleEvent(occurredAt));
                agentSession.RefreshContextUsage();
                agentSession.RefreshUsage();
                break;
            case SessionErrorEvent { Data.Message: { } message }:
                agentSession.Publish(new AgentErrorEvent(occurredAt, message));
                break;
            case SessionErrorEvent:
                agentSession.Publish(new AgentErrorEvent(occurredAt, "Copilot SDK session error"));
                break;
            default:
                Debug.WriteLine($"Ignoring unknown Copilot SDK event: {sessionEvent.GetType().Name}");
                break;
        }
    }

    private static void PublishDiscoveredSkills(SessionEvent sessionEvent, CopilotSdkAgentSession agentSession, DateTimeOffset occurredAt)
    {
        if (GetProperty(sessionEvent, "Data") is null || GetProperty(GetProperty(sessionEvent, "Data")!, "Skills") is not IEnumerable skills)
            return;

        foreach (var skill in skills)
        {
            var path = GetProperty(skill, "Path") as string;
            if (!string.IsNullOrWhiteSpace(path))
                agentSession.Publish(new AgentSystemMessageEvent(occurredAt, $"Discovered {path}"));
        }
    }

    private static void PublishSkillInvocation(SessionEvent sessionEvent, CopilotSdkAgentSession agentSession, DateTimeOffset occurredAt)
    {
        var name = GetProperty(GetProperty(sessionEvent, "Data"), "Name") as string;
        if (!string.IsNullOrWhiteSpace(name))
            agentSession.Publish(new AgentSkillInvokedEvent(occurredAt, name));
    }

    private static object? GetProperty(object? value, string name) => value?.GetType().GetProperty(name)?.GetValue(value);

}



