namespace squad.Handoffs;

/// <summary>Reports that a role's handoff queue still contains one or more legacy, pre-JSON ".handoff" artifacts.</summary>
public sealed class LegacyHandoffQueueException : Exception
{
    public LegacyHandoffQueueException(string message) : base(message)
    {
    }
}
