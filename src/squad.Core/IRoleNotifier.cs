using global::squad.Agent;

namespace squad.Core;

public interface IRoleNotifier
{
    Task NotifyAsync(RoleRow role, CancellationToken cancellationToken = default);
}



