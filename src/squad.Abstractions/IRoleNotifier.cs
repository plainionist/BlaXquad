using global::squad.Agent;

namespace squad.Abstractions;

public interface IRoleNotifier
{
    Task NotifyAsync(RoleRow role, CancellationToken cancellationToken = default);
}



