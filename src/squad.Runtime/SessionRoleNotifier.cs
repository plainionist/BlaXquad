using squad.Application;
using squad.Domain;
using squad.Handoffs.Delivery;

namespace squad.Runtime;

/// <summary>
/// Routes handoff wake-ups through one squad generation''s active-session admission, the same authority interactive
/// commands use, so a role with no admissible session simply fails the same way an interactive command would. It is
/// created by and bound to that generation, so a wake-up can never reach a replacement squad.
/// </summary>
internal sealed class SessionRoleNotifier : IRoleNotifier
{
    private const string myWakeMessage = "You have new handoff mail. If idle, run squad ready-for-next.";
    private readonly SquadMembers myMembers;

    internal SessionRoleNotifier(SquadMembers members)
    {
        myMembers = members;
    }

    public Task NotifyAsync(SquadMemberId role, CancellationToken cancellationToken = default) =>
        myMembers.SendHarnessAsync(role, myWakeMessage, cancellationToken);
}
