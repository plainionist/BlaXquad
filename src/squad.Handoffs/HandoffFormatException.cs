namespace squad.Handoffs;

/// <summary>Reports a handoff document that is malformed, uses an unsupported schema version, or combines an
/// invalid handoff kind and variant-data pairing.</summary>
public sealed class HandoffFormatException : Exception
{
    public HandoffFormatException(string message) : base(message)
    {
    }

    public HandoffFormatException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
