using squad.AgentProvider.Abstractions;

namespace squad.Specs.Support;

/// <summary>
/// Adapts an already-constructed recording backend to the process-time <see cref="IAgentProviderFactory"/>
/// contract used by production composition, so specs can pre-configure roles and sessions on the backend before
/// <see cref="squad.Host.Runtime.SquadApplication"/> starts.
/// </summary>
public sealed class RecordingAgentProviderFactory : IAgentProviderFactory
{
    private readonly IAgentBackend myBackend;

    public RecordingAgentProviderFactory(IAgentBackend backend)
    {
        myBackend = backend;
    }

    public string Name => "recording";

    public Task<IAgentBackend> CreateAsync(AgentBackendContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(myBackend);
    }
}
