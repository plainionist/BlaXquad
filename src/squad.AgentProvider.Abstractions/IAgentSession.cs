using squad.AgentProvider.Abstractions.Agents;

namespace squad.AgentProvider.Abstractions;

/// <summary>
/// Represents one squad member's live provider session and its ordered event stream. Completion reports terminal
/// provider failure as well as normal shutdown.
/// </summary>
public interface IAgentSession : IAsyncDisposable
{
    string Member { get; }
    string SessionId { get; }
    /// <summary>Completes when the provider session terminates and faults when that termination is unsuccessful.</summary>
    Task Completion { get; }
    /// <summary>Reads provider events in publication order until the session completes or the reader is cancelled.</summary>
    IAsyncEnumerable<AgentEvent> Events(CancellationToken cancellationToken = default);
    Task SendAsync(string prompt, CancellationToken cancellationToken = default);
    /// <summary>Sends host-authored context while preserving it as a distinct harness message in the transcript.</summary>
    Task SendHarnessAsync(string prompt, CancellationToken cancellationToken = default);
    /// <summary>Aborts the current operation without terminating the session, which remains available for later prompts.</summary>
    Task AbortAsync(CancellationToken cancellationToken = default);
    Task RespondToPermissionAsync(string requestId, AgentPermissionResponse response, CancellationToken cancellationToken = default);
    Task RespondToInputAsync(string requestId, AgentInputResponse response, CancellationToken cancellationToken = default);
    Task RespondToElicitationAsync(string requestId, AgentElicitationResponse response, CancellationToken cancellationToken = default);
    /// <summary>Cancels every unresolved provider interaction without terminating the session itself.</summary>
    Task CancelPendingInteractionsAsync(CancellationToken cancellationToken = default);
}

