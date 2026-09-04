using global::squad.Agent.Configuration;

namespace squad.Core;

public interface IRoleNotifier
{
    Task NotifyAsync(RoleRow role, CancellationToken cancellationToken = default);
}



