namespace squad.Ui.Abstractions;

/// <summary>
/// Narrow workspace-scoped capability surface for optional external tools launched from the dashboard - currently
/// only the configured Git history viewer. Availability reflects whether a configured executable resolved during
/// launch; an omitted or unresolvable configuration is a normal "unavailable" capability, never a startup failure.
/// </summary>
public interface IWorkspaceTools
{
    bool GitHistoryAvailable { get; }

    /// <summary>Starts the configured Git history executable in the workspace directory. Throws when Git history
    /// is unavailable or the process fails to start; both are protocol errors to the caller, never a silent
    /// no-op.</summary>
    void OpenGitHistory();
}
