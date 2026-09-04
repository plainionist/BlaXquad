using global::squad.Agent;

namespace squad.Core;

internal interface IRoleNotifier
{
    Task NotifyAsync(RoleRow role, CancellationToken cancellationToken = default);
}



