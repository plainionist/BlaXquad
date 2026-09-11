using squad.AgentProvider.Abstractions;

namespace squad.Host.Runtime;

/// <summary>
/// Defines the ordered external-context and workspace preparation phases that must complete before runtime startup.
/// </summary>
public sealed class SquadStartupPlan
{
    private readonly Func<CancellationToken, Task<AgentBackendContext>> myPrepareContextAsync;
    private readonly Func<IEnumerable<string>> myDiscoverRoles;
    private readonly Func<string> myDiscoverLeader;
    private readonly Action myPrepareWorkspace;
    private readonly Func<bool, CancellationToken, Task> myPrepareConfiguredWorktreesForLaunchAsync;
    private readonly Action myPrepareHandoffDirs;
    private readonly bool myContinueLaunch;

    public SquadStartupPlan(
        Func<IEnumerable<string>> discoverRoles,
        Func<string> discoverLeader,
        Action prepareWorkspace,
        Func<bool, CancellationToken, Task> prepareConfiguredWorktreesForLaunchAsync,
        Action prepareHandoffDirs,
        bool continueLaunch,
        Func<CancellationToken, Task<AgentBackendContext>> prepareContextAsync)
    {
        myDiscoverRoles = discoverRoles;
        myDiscoverLeader = discoverLeader;
        myPrepareWorkspace = prepareWorkspace;
        myPrepareConfiguredWorktreesForLaunchAsync = prepareConfiguredWorktreesForLaunchAsync;
        myPrepareHandoffDirs = prepareHandoffDirs;
        myContinueLaunch = continueLaunch;
        myPrepareContextAsync = prepareContextAsync;
    }

    /// <summary>
    /// Prepares external context (git repository, configuration) and returns the prepared agent backend context.
    /// Runs before role discovery.
    /// </summary>
    public Task<AgentBackendContext> PrepareContextAsync(CancellationToken cancellationToken) =>
        myPrepareContextAsync(cancellationToken);

    /// <summary>Discovers the configured roles. Must only be called after <see cref="PrepareContextAsync"/> completes.</summary>
    public IEnumerable<string> DiscoverRoles() => myDiscoverRoles();

    /// <summary>Discovers the configured leader role name. Must only be called after <see cref="PrepareContextAsync"/> completes.</summary>
    public string DiscoverLeader() => myDiscoverLeader();

    public void PrepareWorkspace() => myPrepareWorkspace();

    public Task PrepareConfiguredWorktreesForLaunchAsync(CancellationToken cancellationToken) =>
        myPrepareConfiguredWorktreesForLaunchAsync(myContinueLaunch, cancellationToken);

    public void PrepareHandoffDirs() => myPrepareHandoffDirs();
}
