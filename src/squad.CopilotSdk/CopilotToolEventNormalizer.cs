using global::squad.AgentProvider.Abstractions.Agents;
using GitHub.Copilot;
using System.Text.Json;

#pragma warning disable GHCP001

namespace squad.CopilotSdk;

internal sealed class CopilotToolEventNormalizer
{
    private readonly CopilotSdkAgentSession myAgentSession;
    private readonly string myWorkingDirectory;
    private readonly CopilotToolOutputNormalizer myOutputNormalizer = new();
    private readonly Dictionary<string, string> myToolNames = new(StringComparer.Ordinal);
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
                lock (myStateLock)
                    myToolNames[start.Data.ToolCallId] = start.Data.ToolName;
                myOutputNormalizer.Start(start.Data.ToolCallId);
                myAgentSession.Publish(new AgentToolStartedEvent(
                    occurredAt,
                    start.Data.ToolCallId,
                    start.Data.ToolName,
                    JsonSerializer.Serialize(start.Data.Arguments),
                    WorkingDirectory: myWorkingDirectory));
                return true;
            case ToolExecutionPartialResultEvent partial:
                var normalizedOutput = myOutputNormalizer.Apply(
                    partial.Data.ToolCallId,
                    partial.Data.PartialOutput);
                if (normalizedOutput is not null)
                    myAgentSession.Publish(new AgentToolOutputChangedEvent(
                        occurredAt,
                        partial.Data.ToolCallId,
                        normalizedOutput));
                return true;
            case ToolExecutionProgressEvent progress:
                myAgentSession.Publish(new AgentToolProgressEvent(
                    occurredAt,
                    progress.Data.ToolCallId,
                    progress.Data.ProgressMessage));
                return true;
            case ToolExecutionCompleteEvent complete:
                var (toolName, displayOutputFallback, contentFallback) = Complete(complete);
                myAgentSession.Publish(new AgentToolCompletedEvent(
                    occurredAt,
                    complete.Data.ToolCallId,
                    toolName,
                    complete.Data.Success,
                    displayOutputFallback,
                    contentFallback));
                return true;
            case AssistantServerToolProgressEvent serverProgress:
                myAgentSession.Publish(new AgentToolProgressEvent(
                    occurredAt,
                    $"server:{serverProgress.Data.Kind}:{serverProgress.Data.OutputIndex}",
                    serverProgress.Data.Status));
                return true;
            default:
                return false;
        }
    }

    private (string ToolName, string? DisplayOutputFallback, string? ContentFallback) Complete(
        ToolExecutionCompleteEvent complete)
    {
        lock (myStateLock)
        {
            var streamedOutput = myOutputNormalizer.Complete(complete.Data.ToolCallId);
            var toolName = complete.Data.ToolDescription?.Name
                ?? (myToolNames.Remove(complete.Data.ToolCallId, out var startedToolName)
                    ? startedToolName
                    : "tool");
            myToolNames.Remove(complete.Data.ToolCallId);
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



