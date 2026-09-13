using squad.AgentProvider.Abstractions.Agents;
using squad.Domain;
using squad.Ui.Abstractions;
using System.Text.Json;

namespace squad.Application;

/// <summary>
/// Projects a provider <see cref="AgentEvent"/> onto a <see cref="SquadMember"/> and its transcript.
/// Stateless, synchronous, and deterministic: it mutates only the supplied member aggregate. Callers are
/// responsible for admission checks and for holding the member's lock.
/// </summary>
internal static class SquadMemberEventProjector
{
    public static TranscriptUpdate? Project(SquadMember member, AgentEvent agentEvent)
    {
        member.EventCount++;
        member.LastEventAt = agentEvent.OccurredAt;
        TranscriptUpdate? transcriptUpdate = null;

        switch (agentEvent)
        {
            case AgentStartedEvent:
                member.Status = SquadMemberStatus.Running;
                member.IsWorking = false;
                transcriptUpdate = AddTranscriptEntry(member, agentEvent.OccurredAt, TranscriptSource.Harness, "Session started.");
                break;
            case AgentStoppedEvent:
                member.Status = SquadMemberStatus.Stopped;
                member.IsWorking = false;
                transcriptUpdate = AddTranscriptEntry(member, agentEvent.OccurredAt, TranscriptSource.Harness, "Session stopped.");
                break;
            case AgentErrorEvent error:
                member.Status = SquadMemberStatus.Error;
                member.IsWorking = false;
                member.Error = error.Message;
                transcriptUpdate = AddTranscriptEntry(member, agentEvent.OccurredAt, TranscriptSource.Error, error.Message);
                break;
            case AgentIdleEvent:
                member.Status = SquadMemberStatus.Idle;
                member.IsWorking = false;
                member.Transcript.FinalizeAssistantEntry();
                member.Transcript.FinalizeReasoningEntry();
                break;
            case AgentUserMessageEvent message:
                member.IsWorking = true;
                member.Transcript.FinalizeAssistantEntry();
                member.Transcript.FinalizeReasoningEntry();
                transcriptUpdate = AddTranscriptEntry(member, message.OccurredAt, TranscriptSource.User, message.Content);
                break;
            case AgentHarnessMessageEvent message:
                transcriptUpdate = AddTranscriptEntry(member, message.OccurredAt, TranscriptSource.Harness, message.Content);
                break;
            case AgentReasoningEvent reasoning:
                member.IsWorking = true;
                transcriptUpdate = ApplyReasoning(member, reasoning);
                break;
            case AgentAssistantMessageEvent message:
                member.IsWorking = true;
                transcriptUpdate = ApplyAssistantMessage(member, message);
                break;
            case AgentSubagentStartedEvent subagent:
                member.IsWorking = true;
                transcriptUpdate = AddTranscriptEntry(
                    member,
                    subagent.OccurredAt,
                    TranscriptSource.Subagent,
                    DescribeSubagent(subagent));
                break;
            case AgentSkillInvokedEvent skill:
                member.IsWorking = true;
                transcriptUpdate = AddTranscriptEntry(
                    member,
                    skill.OccurredAt,
                    TranscriptSource.Tool,
                    $"using skill({skill.Name})");
                break;
            case AgentToolStartedEvent tool:
                member.IsWorking = true;

                if (IsSubagentPlumbingTool(tool.ToolName) ||
                    tool.ToolName.Equals("skill", StringComparison.OrdinalIgnoreCase))
                {

                    break;
                }

                var isRead = tool.IsRead || IsReadTool(tool.ToolName);
                var suppressOutput = isRead;
                var toolDescription = isRead ? DescribeRead(tool) : DescribeToolStart(tool);

                if (!string.IsNullOrWhiteSpace(toolDescription))
                {
                    transcriptUpdate = member.Transcript.StartTool(
                        tool.ToolCallId,
                        tool.ToolName,
                        suppressOutput,
                        isRead && !HasReadRange(tool),
                        new TranscriptEntry(
                            tool.OccurredAt,
                            isRead ? TranscriptSource.Read : TranscriptSource.Tool,
                            toolDescription));
                    member.ActiveTool = tool.ToolName;
                }

                break;
            case AgentToolCompletedEvent tool:
                var completion = member.Transcript.CompleteTool(
                    tool.ToolCallId,
                    tool.DisplayOutputFallback,
                    tool.ContentFallback);

                if (completion is not null)
                {
                    transcriptUpdate = completion.Update;
                    member.ActiveTool = completion.ActiveTool;
                }

                break;
            case AgentToolOutputChangedEvent output:
                member.IsWorking = true;
                transcriptUpdate = member.Transcript.ChangeToolOutput(
                    output.ToolCallId,
                    output.Output);
                break;
            case AgentToolProgressEvent progress:
                member.IsWorking = true;
                transcriptUpdate = member.Transcript.ChangeToolProgress(
                    progress.ToolCallId,
                    progress.Progress);
                break;
            case AgentSessionConfigurationEvent configuration:
                member.Model = configuration.Model;
                member.Effort = configuration.Effort;

                if (member.ContextLimitTokens is null or <= 0)
                {
                    member.ContextLimitTokens = GetModelContextWindowLimit(member.Model, 0);
                }

                break;
            case AgentSessionModelChangedEvent model:
                member.Model = model.Model;
                member.Effort = model.Effort;
                member.ContextLimitTokens = GetModelContextWindowLimit(member.Model, member.ContextLimitTokens ?? 0);
                break;
            case AgentSessionUsageEvent usage:
                member.AicUsed = Math.Max(member.AicUsed ?? 0, usage.AicUsed);
                break;
            case AgentContextUsageEvent usage:
                member.ContextUsedTokens = usage.UsedTokens;
                member.ContextLimitTokens = GetModelContextWindowLimit(member.Model, usage.LimitTokens);
                break;
            case AgentSystemMessageEvent message:
                transcriptUpdate = AddTranscriptEntry(member, message.OccurredAt, TranscriptSource.System, message.Content);
                break;
            case AgentPermissionRequest permission:
                transcriptUpdate = member.RegisterPermission(
                    permission,
                    new TranscriptEntry(permission.OccurredAt, TranscriptSource.Harness, $"Permission required: {permission.Description}."));
                break;
            case AgentInputRequest input:
                transcriptUpdate = member.RegisterInput(
                    input,
                    new TranscriptEntry(input.OccurredAt, TranscriptSource.Harness, input.Prompt));
                break;
            case AgentElicitationRequest elicitation:
                transcriptUpdate = member.RegisterElicitation(
                    elicitation,
                    new TranscriptEntry(elicitation.OccurredAt, TranscriptSource.Harness, elicitation.Prompt));
                break;
        }

        return transcriptUpdate;
    }

    private static TranscriptUpdate ApplyAssistantMessage(SquadMember member, AgentAssistantMessageEvent message)
    {

        if (message.IsDelta)
        {
            return member.Transcript.AppendAssistantEntry(message.OccurredAt, message.Content);
        }

        return member.Transcript.CompleteAssistantEntry(message.OccurredAt, message.Content);
    }

    private static TranscriptUpdate ApplyReasoning(SquadMember member, AgentReasoningEvent reasoning)
    {

        if (reasoning.IsDelta)
        {
            return member.Transcript.AppendReasoningEntry(reasoning.OccurredAt, reasoning.Content);
        }

        return member.Transcript.CompleteReasoningEntry(reasoning.OccurredAt, reasoning.Content);
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
        {
            return tool.ToolName;
        }

        if (TryParseToolArguments(tool.Arguments, out var arguments))
        {

            if (arguments.ValueKind is JsonValueKind.Object &&
                arguments.TryGetProperty("command", out var command) &&
                command.ValueKind is JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(command.GetString()))
            {

                var cmd = command.GetString()!;

                if (tool.ToolName.Contains(cmd, StringComparison.Ordinal))
                {
                    return tool.ToolName;
                }

                if (KnownShellRunners.Contains(tool.ToolName))
                {
                    return $"{tool.ToolName} {cmd}";
                }

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
        {
            return tool.ToolName;
        }

        if (!TryParseToolArguments(tool.Arguments, out var arguments))
        {
            return tool.ToolName;
        }

        var path = ReadPath(arguments);

        if (string.IsNullOrWhiteSpace(path))
        {
            return tool.ToolName;
        }

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
        {
            return false;
        }

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
        {

            if (arguments.TryGetProperty(name, out var value) && value.TryGetInt32(out line))
            {
                return true;
            }

        }

        line = 0;
        return false;
    }

    private static string? ReadPath(JsonElement arguments)
    {

        if (arguments.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        foreach (var propertyName in new[] { "path", "filePath", "file_path", "filename" })

        {

            if (arguments.TryGetProperty(propertyName, out var property) && property.ValueKind is JsonValueKind.String)
            {
                return property.GetString();
            }

        }

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
        SquadMember member,
        DateTimeOffset occurredAt,
        TranscriptSource source,
        string content,
        bool protect = false) =>
        member.Transcript.AddTranscriptEntry(new TranscriptEntry(occurredAt, source, content), protect);

    private static long GetModelContextWindowLimit(string? model, long reportedLimit)
    {
        var knownLimit = GetKnownModelLimit(model);

        if (knownLimit > 0)
        {
            return Math.Max(knownLimit, reportedLimit);
        }

        return reportedLimit > 0 ? reportedLimit : 128000;
    }

    private static long GetKnownModelLimit(string? model)
    {

        if (string.IsNullOrWhiteSpace(model))
        {
            return 0;
        }

        var lower = model.ToLowerInvariant();

        if (lower.Contains("claude"))
        {
            return 200000;
        }

        if (lower.Contains("gpt-4o") || lower.Contains("gpt-4.5") || lower.Contains("o1") || lower.Contains("o3"))
        {
            return 128000;
        }

        return 128000;
    }
}
