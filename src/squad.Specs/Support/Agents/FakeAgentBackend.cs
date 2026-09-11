using squad.AgentProvider.Abstractions;
using squad.Specs.Support.Agents.Control;

namespace squad.Specs.Support.Agents;

/// <summary>
/// Provider-side backend for <see cref="FakeAgentProviderFactory"/>. Creates one runtime generation per the
/// production <see cref="IAgentBackend"/> contract; owns no resources of its own beyond the context it hands to
/// each runtime it creates. When the environment names
/// <see cref="FakeProviderControlServer.FailBeforeRuntimeEnvironmentVariable"/>, fails before ever creating a
/// runtime - mirroring a real provider whose runtime never becomes available - so a specification can prove
/// squad-hq reports a clean diagnostic and terminates without ever establishing a session or reaching readiness.
/// Implements <see cref="IAgentBackendFailureSource"/> so a specification can also fault this backend at any
/// point after readiness through the fake-provider control pipe's "fail-backend" command, mirroring a real
/// provider's fatal, backend-wide failure that is independent of any individual role's session.
/// </summary>
internal sealed class FakeAgentBackend(AgentBackendContext context) : IAgentBackend, IAgentBackendFailureSource
{
    private readonly TaskCompletionSource myFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Failure => myFailure.Task;

    public Task<IAgentRuntime> CreateRuntimeAsync(CancellationToken cancellationToken = default)
    {
        if (Environment.GetEnvironmentVariable(FakeProviderControlServer.FailBeforeRuntimeEnvironmentVariable) is not null)
        {
            throw new InvalidOperationException("fake provider failed before its runtime became available");
        }
        return Task.FromResult<IAgentRuntime>(new FakeAgentRuntime(context, FailBackend));
    }

    /// <summary>Faults <see cref="Failure"/> with the given message, as a real provider's fatal, backend-wide
    /// failure reported through <see cref="IAgentBackendFailureSource"/> - reachable only through the fake
    /// provider control pipe's "fail-backend" command via the runtime this backend creates, never a product test
    /// hook.</summary>
    private Task FailBackend(string message, CancellationToken cancellationToken)
    {
        myFailure.TrySetException(new InvalidOperationException(message));
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
