using squad.AgentProvider.Abstractions;
using squad.AgentProvider.Abstractions.Agents;
using squad.Application.Interactions;
using squad.Ui.Abstractions;
using System.Text.Json;

namespace squad.Application.Events;

/// <summary>
/// Projects a provider <see cref="AgentEvent"/> onto an <see cref="AgentRoleState"/> and its transcript.
/// Synchronous and deterministic: it mutates only the supplied state, its transcript, and the injected
/// interaction registry. Callers are responsible for admission checks and for holding the role's lock.
/// </summary>
internal sealed class AgentEventProjector
{
    private readonly PendingInteractionRegistry myInteractions;

    public AgentEventProjector(PendingInteractionRegistry interactions)
    {
        myInteractions = interactions;
    }

    public TranscriptUpdate? Project(AgentRoleState state, AgentEvent agentEvent)
    {
        state.EventCount++;
        state.LastEventAt = agentEvent.OccurredAt;
        TranscriptUpdate? transcriptUpdate = null;
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
        return transcriptUpdate;
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
}
