using GitHub.Copilot;
using System.Text.Json;

#pragma warning disable GHCP001

namespace squad.CopilotSdk;

internal static class RawSdkEventTrace
{
    private const string TracePathVariable = "BLAXQUAD_SDK_EVENT_TRACE";
    private static readonly object myTraceLock = new();
    private static readonly Dictionary<string, string> myPreviousPayloads = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> myToolNames = new(StringComparer.Ordinal);
    private static long mySequence;

    public static void Record(SessionEvent sessionEvent)
    {
        var tracePath = Environment.GetEnvironmentVariable(TracePathVariable);
        if (string.IsNullOrWhiteSpace(tracePath))
            return;

        lock (myTraceLock)
        {
            try
            {
                if (!TryDescribe(sessionEvent, out var description))
                    return;
                var payloads = description.Payloads
                    .Select(payload => DescribePayload(description.ToolCallId, payload))
                    .ToArray();
                var record = new
                {
                    sequence = ++mySequence,
                    observedAt = DateTimeOffset.UtcNow,
                    sdkEventType = description.EventType,
                    sdkEventId = GetProperty(sessionEvent, "Id"),
                    toolCallId = description.ToolCallId,
                    toolName = description.ToolName,
                    ephemeral = GetProperty(sessionEvent, "Ephemeral"),
                    payloads,
                };
                var directory = Path.GetDirectoryName(Path.GetFullPath(tracePath));
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.AppendAllText(tracePath, JsonSerializer.Serialize(record) + Environment.NewLine);
                if (sessionEvent is ToolExecutionCompleteEvent && description.ToolCallId is not null)
                {
                    myPreviousPayloads.Remove(description.ToolCallId);
                    myToolNames.Remove(description.ToolCallId);
                }
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to record raw Copilot SDK event: {exception}");
            }
        }
    }

    private static bool TryDescribe(SessionEvent sessionEvent, out EventDescription description)
    {
        switch (sessionEvent)
        {
            case ToolExecutionStartEvent start:
                myToolNames[start.Data.ToolCallId] = start.Data.ToolName;
                description = new("tool.execution_start", start.Data.ToolCallId, start.Data.ToolName, []);
                return true;
            case ToolExecutionPartialResultEvent partial:
                description = new(
                    "tool.execution_partial_result",
                    partial.Data.ToolCallId,
                    GetToolName(partial.Data.ToolCallId),
                    [new("partialOutput", partial.Data.PartialOutput)]);
                return true;
            case ToolExecutionProgressEvent progress:
                description = new(
                    "tool.execution_progress",
                    progress.Data.ToolCallId,
                    GetToolName(progress.Data.ToolCallId),
                    [new("progressMessage", progress.Data.ProgressMessage)]);
                return true;
            case ToolExecutionCompleteEvent complete:
                var payloads = new List<Payload>();
                if (complete.Data.Result?.Content is { } content)
                    payloads.Add(new("content", content));
                if (complete.Data.Result?.DetailedContent is { } detailedContent)
                    payloads.Add(new("detailedContent", detailedContent));
                description = new(
                    "tool.execution_complete",
                    complete.Data.ToolCallId,
                    complete.Data.ToolDescription?.Name ?? GetToolName(complete.Data.ToolCallId),
                    payloads);
                return true;
            case AssistantServerToolProgressEvent serverProgress:
                description = new(
                    "assistant.server_tool_progress",
                    GetProperty(serverProgress.Data, "ToolCallId") as string,
                    GetProperty(serverProgress.Data, "Kind")?.ToString(),
                    [new("status", serverProgress.Data.Status)]);
                return true;
            default:
                description = default!;
                return false;
        }
    }

    private static object DescribePayload(string? toolCallId, Payload payload)
    {
        var previous = toolCallId is not null && myPreviousPayloads.TryGetValue(toolCallId, out var value)
            ? value
            : null;
        var (relationship, appendedContent) = Classify(previous, payload.Content);
        if (toolCallId is not null)
            myPreviousPayloads[toolCallId] = payload.Content;
        return new
        {
            field = payload.Field,
            length = payload.Content.Length,
            escaped = JsonSerializer.Serialize(payload.Content),
            relationship,
            appendedContent = appendedContent is null ? null : JsonSerializer.Serialize(appendedContent),
        };
    }

    private static (string Relationship, string? AppendedContent) Classify(string? previous, string current)
    {
        if (previous is null)
            return ("first payload", null);
        if (string.Equals(previous, current, StringComparison.Ordinal))
            return ("exact duplicate", "");
        if (current.StartsWith(previous, StringComparison.Ordinal))
            return ("cumulative snapshot", current[previous.Length..]);
        if (previous.StartsWith(current, StringComparison.Ordinal))
            return ("rewrite/non-prefix change", null);
        return ("independent delta", null);
    }

    private static string? GetToolName(string toolCallId) =>
        myToolNames.TryGetValue(toolCallId, out var toolName) ? toolName : null;

    private static object? GetProperty(object? value, string name) =>
        value?.GetType().GetProperty(name)?.GetValue(value);

    private sealed record EventDescription(
        string EventType,
        string? ToolCallId,
        string? ToolName,
        IReadOnlyList<Payload> Payloads);

    private sealed record Payload(string Field, string Content);
}



