using squad.AgentProvider.Abstractions.Agents;

namespace squad.AgentProvider.Abstractions;

public interface IAgentSession : IAsyncDisposable
{
    string Role { get; }
    string SessionId { get; }
    Task Completion { get; }
    IAsyncEnumerable<AgentEvent> Events(CancellationToken cancellationToken = default);
    Task SendAsync(string prompt, CancellationToken cancellationToken = default);
    Task SendHarnessAsync(string prompt, CancellationToken cancellationToken = default);
    Task AbortAsync(CancellationToken cancellationToken = default);
    Task RespondToPermissionAsync(string requestId, AgentPermissionResponse response, CancellationToken cancellationToken = default);
    Task RespondToInputAsync(string requestId, AgentInputResponse response, CancellationToken cancellationToken = default);
    Task RespondToElicitationAsync(string requestId, AgentElicitationResponse response, CancellationToken cancellationToken = default);
    Task CancelPendingInteractionsAsync(CancellationToken cancellationToken = default);
}



