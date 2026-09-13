using squad.AgentProvider.Abstractions;
using squad.AgentProvider.Abstractions.Agents;
using GitHub.Copilot;
using System.Text.Json;

#pragma warning disable GHCP001

namespace squad.AgentProvider.CopilotSdk;

/// <summary>
/// Correlates SDK tool lifecycle events and emits provider-neutral tool events with normalized output semantics.
/// </summary>
internal sealed class CopilotToolEventNormalizer
{
    private readonly CopilotSdkAgentSession myAgentSession;
    private readonly string myWorkingDirectory;
    private readonly CopilotToolOutputNormalizer myOutputNormalizer = new();
    private readonly Dictionary<ToolCallId, string> myToolNames = [];
    private readonly object myStateLock = new();

    public CopilotToolEventNormalizer(CopilotSdkAgentSession agentSession, string workingDirectory)
    {
        myAgentSession = agentSession;
        myWorkingDirectory = workingDirectory;
    }

    public bool TryPublish(SessionEvent sessionEvent)
    {
        var occurredAt = DateTimeOffset.UtcNow;

        switch (sessionEvent)
        {
            case ToolExecutionStartEvent start:
                var startedToolCallId = new ToolCallId(start.Data.ToolCallId);
                lock (myStateLock)
                    myToolNames[startedToolCallId] = start.Data.ToolName;
                myOutputNormalizer.Start(startedToolCallId);
                myAgentSession.Publish(new AgentToolStartedEvent(
                    occurredAt,
                    startedToolCallId,
                    start.Data.ToolName,
                    JsonSerializer.Serialize(start.Data.Arguments),
                    WorkingDirectory: myWorkingDirectory));
                return true;
            case ToolExecutionPartialResultEvent partial:
                var partialToolCallId = new ToolCallId(partial.Data.ToolCallId);
                var normalizedOutput = myOutputNormalizer.Apply(
                    partialToolCallId,
                    partial.Data.PartialOutput);

                if (normalizedOutput is not null)
                {
                    myAgentSession.Publish(new AgentToolOutputChangedEvent(
                        occurredAt,
                        partialToolCallId,
                        normalizedOutput));
                }

                return true;
            case ToolExecutionProgressEvent progress:
                myAgentSession.Publish(new AgentToolProgressEvent(
                    occurredAt,
                    new ToolCallId(progress.Data.ToolCallId),
                    progress.Data.ProgressMessage));
                return true;
            case ToolExecutionCompleteEvent complete:
                var completedToolCallId = new ToolCallId(complete.Data.ToolCallId);
                var (toolName, displayOutputFallback, contentFallback) = Complete(completedToolCallId, complete);
                myAgentSession.Publish(new AgentToolCompletedEvent(
                    occurredAt,
                    completedToolCallId,
                    toolName,
                    complete.Data.Success,
                    displayOutputFallback,
                    contentFallback));
                return true;
            case AssistantServerToolProgressEvent serverProgress:
                myAgentSession.Publish(new AgentToolProgressEvent(
                    occurredAt,
                    new ToolCallId($"server:{serverProgress.Data.Kind}:{serverProgress.Data.OutputIndex}"),
                    serverProgress.Data.Status));
                return true;
            default:
                return false;
        }
    }

    private (string ToolName, string? DisplayOutputFallback, string? ContentFallback) Complete(
        ToolCallId toolCallId, ToolExecutionCompleteEvent complete)
    {
        lock (myStateLock)
        {
            var streamedOutput = myOutputNormalizer.Complete(toolCallId);
            var toolName = complete.Data.ToolDescription?.Name
                ?? (myToolNames.Remove(toolCallId, out var startedToolName)
                    ? startedToolName
                    : "tool");
            myToolNames.Remove(toolCallId);
            var displayOutputFallback = streamedOutput
                ? null
                : complete.Data.Result?.DetailedContent
                    ?? complete.Data.Result?.Content
                    ?? complete.Data.Error?.Message;
            var contentFallback = streamedOutput
                ? null
                : complete.Data.Result?.Content;
            return (toolName, displayOutputFallback, contentFallback);
        }
    }
}
