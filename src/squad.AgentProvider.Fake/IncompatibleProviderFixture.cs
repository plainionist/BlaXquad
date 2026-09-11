namespace squad.AgentProvider.Fake;

/// <summary>Public type that does not implement <see cref="squad.AgentProvider.Abstractions.IAgentProviderFactory"/>,
/// used to prove that "--provider" selection rejects an incompatible type with a clear diagnostic.</summary>
public sealed class IncompatibleProviderFixture
{
}
