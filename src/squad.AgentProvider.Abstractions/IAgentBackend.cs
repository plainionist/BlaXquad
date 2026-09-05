namespace squad.AgentProvider.Abstractions;

// A backend is a per-process runtime-generation factory; it owns no session or provider-client resources itself.
// Disposal is a no-op safety net for implementations with nothing generation-scoped to release.
public interface IAgentBackend : IAsyncDisposable
{
    Task<IAgentRuntime> CreateRuntimeAsync(CancellationToken cancellationToken = default);
}



