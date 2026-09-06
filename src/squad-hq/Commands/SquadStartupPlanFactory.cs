using squad.Host.Runtime;
using squad.Workspaces;

namespace squadHQ.Commands;

/// <summary>
/// Builds a <see cref="SquadStartupPlan"/> backed by the concrete workspace context and preparer used in
/// production and tests. Lives in headquarters (not squad.Host.Runtime) because it depends on the workspace
/// assembly, which squad.Host.Runtime must not reference.
/// </summary>
public static class SquadStartupPlanFactory
{
    public static SquadStartupPlan ForWorkspace(
        Ctx context,
        WorkspacePreparer workspacePreparer,
        Func<CancellationToken, Task>? prepareContextAsync = null) =>
        new(
            discoverRoles: () => context.Roles.Select(role => role.Role),
            prepareWorkspace: () => workspacePreparer.PrepareWorkspace(context),
            prepareConfiguredWorktreesForLaunchAsync: (continueLaunch, cancellationToken) =>
                workspacePreparer.PrepareConfiguredWorktreesForLaunchAsync(context, continueLaunch, cancellationToken),
            prepareHandoffDirs: () => workspacePreparer.PrepareHandoffDirs(context),
            continueLaunch: context.ContinueLaunch,
            prepareContextAsync: prepareContextAsync);
}
