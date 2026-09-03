namespace squad.Abstractions;

public interface IAgentBackendFailureSource
{
    Task Failure { get; }
}



