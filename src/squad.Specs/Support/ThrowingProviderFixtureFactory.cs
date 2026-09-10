using squad.AgentProvider.Abstractions;

namespace squad.Specs.Support;

/// <summary>Valid <see cref="IAgentProviderFactory"/> implementation whose constructor always throws, used to
/// prove that "--provider" selection surfaces construction failures with a clear diagnostic.</summary>
public sealed class ThrowingProviderFixtureFactory : IAgentProviderFactory
{
    public ThrowingProviderFixtureFactory() =>
        throw new InvalidOperationException("fixture construction failure");

    public string Name => "throwing-fixture";

    public Task<IAgentBackend> CreateAsync(AgentBackendContext context, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
