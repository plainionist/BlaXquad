using squad.AgentProvider.Abstractions;

namespace squad.AgentProvider.CopilotSdk;

/// <summary>
/// Normalizes SDK tool output that may arrive as cumulative snapshots or independent deltas into cumulative
/// content for each tool call.
/// </summary>
internal sealed class CopilotToolOutputNormalizer
{
    private readonly Dictionary<ToolCallId, ToolOutputState> myOutputs = [];
    private readonly object myStateLock = new();

    public void Start(ToolCallId toolCallId)
    {
        lock (myStateLock)
            myOutputs[toolCallId] = new(null, StreamingMode.Unknown);
    }

    /// <summary>
    /// Applies one partial output and returns the changed cumulative value, or <see langword="null"/> for a
    /// duplicate snapshot.
    /// </summary>
    public string? Apply(ToolCallId toolCallId, string partialOutput)
    {
        lock (myStateLock)
        {
            if (!myOutputs.TryGetValue(toolCallId, out var state))
            {
                state = new(null, StreamingMode.Unknown);
            }
            if (state.Output is null)
            {
                myOutputs[toolCallId] = state with { Output = partialOutput };
                return partialOutput;
            }
            if (string.Equals(partialOutput, state.Output, StringComparison.Ordinal))
            {
                return null;
            }

            var mode = state.Mode is StreamingMode.Unknown
                ? partialOutput.StartsWith(state.Output, StringComparison.Ordinal)
                    ? StreamingMode.Snapshot
                    : StreamingMode.Delta
                : state.Mode;
            var normalizedOutput = mode is StreamingMode.Snapshot
                ? partialOutput
                : state.Output + partialOutput;
            myOutputs[toolCallId] = new(normalizedOutput, mode);
            return normalizedOutput;
        }
    }

    public bool Complete(ToolCallId toolCallId)
    {
        lock (myStateLock)
            return myOutputs.Remove(toolCallId, out var state) && state.Output?.Length > 0;
    }

    private enum StreamingMode
    {
        Unknown,
        Snapshot,
        Delta,
    }

    private sealed record ToolOutputState(string? Output, StreamingMode Mode);
}

