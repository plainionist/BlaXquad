using global::squad.Core;
using global::squad.Core.Handoffs;

namespace squadHQ.Commands;

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

    public async Task NotifyAsync(string role, CancellationToken cancellationToken = default)
    {
        _ = mySessions.GetActive(role);
        await myViewModel.SendHarnessAsync(role, myWakeMessage, cancellationToken);
    }
}



