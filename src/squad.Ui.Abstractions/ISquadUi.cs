using System.Text.Json;
using squad.AgentProvider.Abstractions.Agents;

namespace squad.Ui.Abstractions;

/// <summary>Defines the authoritative application operations exposed to a UI protocol adapter.</summary>
public interface ISquadUi
{
    /// <summary>Requests publication of a fresh state snapshot at the indicated scheduling priority.</summary>
    event Action<UiRefreshPriority>? SnapshotRequested;
    JsonElement CreateSnapshot();
    AgentElicitationRequest GetPendingElicitation(string role, string requestId);
    Task SendAsync(string role, string prompt, CancellationToken cancellationToken = default);
    Task AbortAsync(string role, CancellationToken cancellationToken = default);
    Task CompletePermissionAsync(string role, string requestId, bool approved, CancellationToken cancellationToken = default);
    Task CompleteInputAsync(string role, string requestId, string? answer, bool wasFreeform, CancellationToken cancellationToken = default);
    Task CompleteElicitationAsync(string role, string requestId, string action, JsonElement? content, CancellationToken cancellationToken = default);
}


