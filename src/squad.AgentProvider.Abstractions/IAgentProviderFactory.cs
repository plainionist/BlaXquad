namespace squad.AgentProvider.Abstractions;

/// <summary>
/// The sole process-time provider contract: creates the provider-neutral agent backend for one launch from a
/// fully prepared <see cref="AgentBackendContext"/> value.
/// </summary>
public interface IAgentProviderFactory
{
    string Name { get; }

    Task<IAgentBackend> CreateAsync(AgentBackendContext context, CancellationToken cancellationToken);
}


