namespace squad.AgentProvider.Abstractions;

/// <summary>Exposes fatal backend-wide failures that occur outside an individual session's completion path.</summary>
public interface IAgentBackendFailureSource
{
    Task Failure { get; }
}


