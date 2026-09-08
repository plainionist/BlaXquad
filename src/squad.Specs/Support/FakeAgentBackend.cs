using squad.AgentProvider.Abstractions;

namespace squad.Specs.Support;

/// <summary>
/// Provider-side backend for <see cref="FakeAgentProviderFactory"/>. Creates one runtime generation per the
/// production <see cref="IAgentBackend"/> contract; owns no resources of its own beyond the context it hands to
/// each runtime it creates.
/// </summary>
internal sealed class FakeAgentBackend(AgentBackendContext context) : IAgentBackend
{
    public Task<IAgentRuntime> CreateRuntimeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IAgentRuntime>(new FakeAgentRuntime(context));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
