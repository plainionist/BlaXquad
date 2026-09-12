using squad.Domain;

namespace squad.Handoffs.Delivery;

/// <summary>Wakes a role after handoff delivery without coupling filesystem delivery to an agent provider.</summary>
public interface IRoleNotifier
{
    Task NotifyAsync(SquadMemberId role, CancellationToken cancellationToken = default);
}


