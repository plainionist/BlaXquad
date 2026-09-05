namespace squad.Host.Runtime;

/// <summary>
/// Coordinates the phases SquadApplication must complete before starting its runtime controller: preparing
/// external context (the git repository, parsed configuration, and backend preparation), discovering the
/// configured roles only after that preparation completes, and preparing the on-disk workspace (worktrees and
/// handoff directories) for the discovered roles.
/// </summary>
public sealed class SquadStartupPlan
{
    private readonly Func<CancellationToken, Task>? myPrepareContextAsync;
    private readonly Func<IEnumerable<string>> myDiscoverRoles;
    private readonly Action myPrepareWorkspace;
    private readonly Func<bool, CancellationToken, Task> myPrepareConfiguredWorktreesForLaunchAsync;
    private readonly Action myPrepareHandoffDirs;
    private readonly bool myContinueLaunch;

    public SquadStartupPlan(
        Func<IEnumerable<string>> discoverRoles,
        Action prepareWorkspace,
        Func<bool, CancellationToken, Task> prepareConfiguredWorktreesForLaunchAsync,
        Action prepareHandoffDirs,
        bool continueLaunch,
        Func<CancellationToken, Task>? prepareContextAsync = null)
    {
        myDiscoverRoles = discoverRoles;
        myPrepareWorkspace = prepareWorkspace;
        myPrepareConfiguredWorktreesForLaunchAsync = prepareConfiguredWorktreesForLaunchAsync;
        myPrepareHandoffDirs = prepareHandoffDirs;
        myContinueLaunch = continueLaunch;
        myPrepareContextAsync = prepareContextAsync;
    }

    /// <summary>Prepares external context (git repository, configuration, backend). Runs before role discovery.</summary>
    public Task PrepareContextAsync(CancellationToken cancellationToken) =>
        myPrepareContextAsync?.Invoke(cancellationToken) ?? Task.CompletedTask;

    /// <summary>Discovers the configured roles. Must only be called after <see cref="PrepareContextAsync"/> completes.</summary>
    public IEnumerable<string> DiscoverRoles() => myDiscoverRoles();

    public void PrepareWorkspace() => myPrepareWorkspace();

    public Task PrepareConfiguredWorktreesForLaunchAsync(CancellationToken cancellationToken) =>
        myPrepareConfiguredWorktreesForLaunchAsync(myContinueLaunch, cancellationToken);

    public void PrepareHandoffDirs() => myPrepareHandoffDirs();
}
