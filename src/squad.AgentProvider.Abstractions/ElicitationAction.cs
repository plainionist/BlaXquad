namespace squad.AgentProvider.Abstractions;

/// <summary>The host's disposition toward one pending elicitation request.</summary>
public enum ElicitationAction
{
    Accept,
    Decline,
    Cancel,
}
