using squad.AgentProvider.Abstractions;

namespace squad.Specs.Support;

/// <summary>
/// Provider-side backend for <see cref="FakeAgentProviderFactory"/>. Creates one runtime generation per the
/// production <see cref="IAgentBackend"/> contract; owns no resources of its own beyond the context it hands to
/// each runtime it creates. When the environment names
/// <see cref="FakeProviderControlServer.FailBeforeRuntimeEnvironmentVariable"/>, fails before ever creating a
/// runtime - mirroring a real provider whose runtime never becomes available - so a specification can prove
/// squad-hq reports a clean diagnostic and terminates without ever establishing a session or reaching readiness.
/// </summary>
internal sealed class FakeAgentBackend(AgentBackendContext context) : IAgentBackend
{
    public Task<IAgentRuntime> CreateRuntimeAsync(CancellationToken cancellationToken = default)
    {
        if (Environment.GetEnvironmentVariable(FakeProviderControlServer.FailBeforeRuntimeEnvironmentVariable) is not null)
        {
            throw new InvalidOperationException("fake provider failed before its runtime became available");
        }
        return Task.FromResult<IAgentRuntime>(new FakeAgentRuntime(context));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
