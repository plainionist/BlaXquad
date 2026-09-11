namespace squad.Workspaces;

/// <summary>
/// Reports an invalid workspace or configuration discovered while preparing a squad launch: missing configuration
/// or constitution files, invalid squad configuration, a missing required helper script, or an unsafe shared
/// worktree path. Carries an unformatted diagnostic; presentation (for example ANSI coloring) is applied once at
/// the command boundary that translates this exception to a process exit.
/// </summary>
public sealed class WorkspacePreparationException : Exception
{
    public WorkspacePreparationException(string message) : base(message)
    {
    }

    public WorkspacePreparationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
