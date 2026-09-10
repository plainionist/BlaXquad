using squad.AgentProvider.Abstractions;

namespace squad.Specs.Support;

/// <summary>Valid <see cref="IAgentProviderFactory"/> implementation used to prove that an explicit
/// "--provider" descriptor loads and constructs successfully when squad-hq is launched as a real, separate
/// process. It never actually runs a session, since the scenarios that use it only exercise provider selection.</summary>
public sealed class ValidProviderFixtureFactory : IAgentProviderFactory
{
    public string Name => "fixture";

    public Task<IAgentBackend> CreateAsync(AgentBackendContext context, CancellationToken cancellationToken) =>
        throw new NotSupportedException($"{nameof(ValidProviderFixtureFactory)} only proves provider selection; it never runs a session.");
}
