using System.Threading.Channels;
using squad.AgentProvider.Abstractions.Agents;

namespace squad.AgentProvider.Fake;

/// <summary>
/// Test-owned event-stream primitive backing every fixture's <c>IAgentSession.Events(...)</c> (the fake, echo,
/// and controllable provider sessions). Preserves publish order over a plain unbounded
/// <see cref="System.Threading.Channels.Channel{T}"/> - a scenario drives every one of this suite's fixtures
/// itself, so the bounded-capacity, write-timeout, and overload-fault admission control the real production
/// <c>AgentEventChannel</c> needs for an unbounded, uncooperative live provider has no Gherkin-observable outcome
/// here: every "session failure" scenario instead drives failure explicitly through a fixture's own fail-session
/// command, never through sustained channel backpressure.
/// </summary>
internal sealed class TestAgentEventStream : IAsyncDisposable
{
    private readonly Channel<AgentEvent> myChannel = Channel.CreateUnbounded<AgentEvent>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });

    public void Publish(AgentEvent agentEvent) => myChannel.Writer.TryWrite(agentEvent);

    public IAsyncEnumerable<AgentEvent> ReadAllAsync(CancellationToken cancellationToken = default) =>
        myChannel.Reader.ReadAllAsync(cancellationToken);

    public void Complete(Exception? error = null) => myChannel.Writer.TryComplete(error);

    public ValueTask DisposeAsync()
    {
        myChannel.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
