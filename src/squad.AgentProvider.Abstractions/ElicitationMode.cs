namespace squad.AgentProvider.Abstractions;

/// <summary>The elicitation UI a host renders for one pending elicitation request: an inline form, or an
/// externally opened URL.</summary>
public enum ElicitationMode
{
    Form,
    Url,
}
