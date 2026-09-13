namespace squad.AgentProvider.Abstractions;

/// <summary>
/// Creates isolated provider-runtime generations. Each returned runtime, rather than the backend, owns its
/// provider connection and sessions.
/// </summary>
public interface IAgentBackend : IAsyncDisposable
{
    /// <summary>Creates a new runtime generation without reusing resources from an earlier generation.</summary>
    Task<IAgentRuntime> CreateRuntimeAsync(CancellationToken cancellationToken = default);
}
