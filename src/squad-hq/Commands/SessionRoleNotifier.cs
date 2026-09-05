using squad.Application;
using squad.Handoffs.Delivery;

namespace squadHQ.Commands;

public sealed class SessionRoleNotifier : IRoleNotifier
{
    private const string myWakeMessage = "You have new handoff mail. If idle, run squad ready-for-next.";
    private readonly SessionRegistry mySessions;
    private readonly SquadViewModel myViewModel;

    internal SessionRoleNotifier(SessionRegistry sessions, SquadViewModel viewModel)
    {
        mySessions = sessions;
        myViewModel = viewModel;
    }

    public async Task NotifyAsync(string role, CancellationToken cancellationToken = default)
    {
        // Handoff routing uses the same current-generation authority as command dispatch: a role with no leaseable
        // session (unregistered, completed, or the registry no longer accepting work) must not even attempt a
        // wake-up send.
        if (!mySessions.TryLeaseSession(role, out _))
            throw new InvalidOperationException($"No active session for role '{role}'.");
        await myViewModel.SendHarnessAsync(role, myWakeMessage, cancellationToken);
    }
}



