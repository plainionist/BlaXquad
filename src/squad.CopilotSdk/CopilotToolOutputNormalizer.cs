namespace squad.CopilotSdk;

public sealed class CopilotToolOutputNormalizer
{
    private readonly Dictionary<string, ToolOutputState> myOutputs = new(StringComparer.Ordinal);
    private readonly object myStateLock = new();

    public void Start(string toolCallId)
    {
        lock (myStateLock)
            myOutputs[toolCallId] = new(null, StreamingMode.Unknown);
    }

    public string? Apply(string toolCallId, string partialOutput)
    {
        lock (myStateLock)
        {
            if (!myOutputs.TryGetValue(toolCallId, out var state))
                state = new(null, StreamingMode.Unknown);
            if (state.Output is null)
            {
                myOutputs[toolCallId] = state with { Output = partialOutput };
                return partialOutput;
            }
            if (string.Equals(partialOutput, state.Output, StringComparison.Ordinal))
                return null;

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

    public bool Complete(string toolCallId)
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



