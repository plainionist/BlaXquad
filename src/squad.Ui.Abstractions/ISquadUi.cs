using System.Text.Json;
using squad.AgentProvider.Abstractions.Agents;
using squad.Domain;

namespace squad.Ui.Abstractions;

/// <summary>Defines the authoritative application operations exposed to a UI protocol adapter.</summary>
public interface ISquadUi
{
    /// <summary>Requests publication of a fresh state snapshot at the indicated scheduling priority.</summary>
    event Action<UiRefreshPriority>? SnapshotRequested;
    JsonElement CreateSnapshot();
    AgentElicitationRequest GetPendingElicitation(SquadMemberId memberId, string requestId);
    Task SendAsync(SquadMemberId memberId, string prompt, CancellationToken cancellationToken = default);
    Task AbortAsync(SquadMemberId memberId, CancellationToken cancellationToken = default);
    Task CompletePermissionAsync(SquadMemberId memberId, string requestId, bool approved, CancellationToken cancellationToken = default);
    Task CompleteInputAsync(SquadMemberId memberId, string requestId, string? answer, bool wasFreeform, CancellationToken cancellationToken = default);
    Task CompleteElicitationAsync(SquadMemberId memberId, string requestId, string action, JsonElement? content, CancellationToken cancellationToken = default);
}


