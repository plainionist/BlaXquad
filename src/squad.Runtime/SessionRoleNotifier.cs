using squad.Application;
using squad.Handoffs.Delivery;

namespace squad.Runtime;

/// <summary>
/// Routes handoff wake-ups through the application's active-session admission, the same authority interactive
/// commands use, so a role with no admissible session simply fails the same way an interactive command would.
/// </summary>
internal sealed class SessionRoleNotifier : IRoleNotifier
{
    private const string myWakeMessage = "You have new handoff mail. If idle, run squad ready-for-next.";
    private readonly SquadViewModel myViewModel;

    internal SessionRoleNotifier(SquadViewModel viewModel)
    {
        myViewModel = viewModel;
    }

    public Task NotifyAsync(string role, CancellationToken cancellationToken = default) =>
        myViewModel.SendHarnessAsync(role, myWakeMessage, cancellationToken);
}

