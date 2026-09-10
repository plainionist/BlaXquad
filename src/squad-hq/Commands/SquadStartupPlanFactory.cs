using squad.AgentProvider.Abstractions;
using squad.Host.Runtime;
using squad.Workspaces;

namespace squadHQ.Commands;

/// <summary>
/// Composes a <see cref="SquadStartupPlan"/> from the concrete workspace context while keeping workspace
/// dependencies outside the runtime lifecycle assembly.
/// </summary>
static class SquadStartupPlanFactory
{
    public static SquadStartupPlan ForWorkspace(
        Ctx context,
        WorkspacePreparer workspacePreparer,
        Func<CancellationToken, Task<AgentBackendContext>> prepareContextAsync) =>
        new(
            discoverRoles: () => context.Roles.Select(role => role.Role),
            prepareWorkspace: () => workspacePreparer.PrepareWorkspace(context),
            prepareConfiguredWorktreesForLaunchAsync: (continueLaunch, cancellationToken) =>
                workspacePreparer.PrepareConfiguredWorktreesForLaunchAsync(context, continueLaunch, cancellationToken),
            prepareHandoffDirs: () => workspacePreparer.PrepareHandoffDirs(context),
            continueLaunch: context.ContinueLaunch,
            prepareContextAsync: prepareContextAsync);
}
