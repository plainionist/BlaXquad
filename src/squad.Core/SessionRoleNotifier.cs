using global::squad.Agent.Configuration;
namespace squad.Core;

public sealed class SessionRoleNotifier : IRoleNotifier
{
    private const string myWakeMessage = "You have new handoff mail. If idle, run squad ready-for-next.";
    private readonly SessionRegistry mySessions;
    private readonly SquadViewModel myViewModel;

    public SessionRoleNotifier(SessionRegistry sessions, SquadViewModel viewModel)
    {
        mySessions = sessions;
        myViewModel = viewModel;
    }

    public async Task NotifyAsync(RoleRow role, CancellationToken cancellationToken = default)
    {
        _ = mySessions.GetActive(role.Role);
        await myViewModel.SendHarnessAsync(role.Role, myWakeMessage, cancellationToken);
    }
}



