using squad.AgentProvider.Abstractions;

namespace squad.AgentProvider.Fake;

/// <summary>
/// Minimal fake <see cref="IAgentProviderFactory"/> loaded into the real, separately launched, published
/// provider-free squad-hq process through the same explicit "--provider" descriptor production providers use. It
/// exists solely to prove the production provider/runtime/session lifecycle establishes and disposes a session
/// correctly when driven by a fake instead of an actual coding agent; a private control transport is added by a
/// later slice (issue 012, slice 7 onward). Provider-side backend, runtime, and session implementations live in
/// their own files.
/// </summary>
public sealed class FakeAgentProviderFactory : IAgentProviderFactory
{
    public string Name => "fake-provider-fixture";

    public Task<IAgentBackend> CreateAsync(AgentBackendContext context, CancellationToken cancellationToken) =>
        Task.FromResult<IAgentBackend>(new FakeAgentBackend(context));
}
