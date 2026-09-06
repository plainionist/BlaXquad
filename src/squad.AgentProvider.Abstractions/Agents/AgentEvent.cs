namespace squad.AgentProvider.Abstractions.Agents;

/// <summary>Base type for provider-neutral observations emitted by an agent session.</summary>
public abstract record AgentEvent(DateTimeOffset OccurredAt);
