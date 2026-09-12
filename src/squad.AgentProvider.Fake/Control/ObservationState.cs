using System.Text.Json;

namespace squad.AgentProvider.Fake.Control;

/// <summary>One observation kind's latest reported data for one role, and how many times an observation of that
/// kind has been recorded. Owns its own cloned <see cref="JsonElement"/>, independent of whatever
/// <see cref="JsonDocument"/> produced it, so it remains valid after that document is disposed.</summary>
internal sealed class ObservationState
{
    internal JsonElement Data { get; private set; }
    internal int Count { get; private set; }

    /// <summary>Constructs the first observed instance of this kind. The caller owns cloning <paramref
    /// name="data"/> before construction.</summary>
    internal ObservationState(JsonElement data)
    {
        Data = data;
        Count = 1;
    }

    /// <summary>Records a repeated observation of this kind, replacing the latest data and incrementing the
    /// count. The caller owns cloning <paramref name="data"/> before recording.</summary>
    internal void Record(JsonElement data)
    {
        Data = data;
        Count++;
    }
}
