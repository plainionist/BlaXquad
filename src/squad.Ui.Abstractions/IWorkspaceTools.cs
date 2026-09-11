namespace squad.Ui.Abstractions;

/// <summary>
/// Narrow workspace-scoped capability surface for optional external tools launched from the dashboard - currently
/// only the configured Git history viewer. Availability reflects whether a configured executable resolved during
/// launch; an omitted or unresolvable configuration is a normal "unavailable" capability, never a startup failure.
/// </summary>
public interface IWorkspaceTools
{
    bool GitHistoryAvailable { get; }

    /// <summary>Resolves the given `gitHistoryCommand` (the first item is the executable, every remaining item is
    /// one exact argument; <see langword="null"/> or empty means the tool was not configured) against the
    /// executable's own working directory, updating <see cref="GitHistoryAvailable"/>. Called exactly once, after
    /// the immutable prepared-launch result is available and before the window starts, so this instance never
    /// re-reads or re-parses configuration itself.</summary>
    void Configure(IReadOnlyList<string>? gitHistoryCommand);

    /// <summary>Starts the configured Git history executable in the workspace directory. Throws when Git history
    /// is unavailable or the process fails to start; both are protocol errors to the caller, never a silent
    /// no-op.</summary>
    void OpenGitHistory();
}
