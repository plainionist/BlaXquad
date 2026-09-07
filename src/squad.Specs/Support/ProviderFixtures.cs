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

/// <summary>Public type that does not implement <see cref="IAgentProviderFactory"/>, used to prove that
/// "--provider" selection rejects an incompatible type with a clear diagnostic.</summary>
public sealed class IncompatibleProviderFixture
{
}

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
