namespace squad.AgentProvider.Abstractions;

public interface IAgentBackendFailureSource
{
    Task Failure { get; }
}



