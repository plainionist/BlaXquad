using squad.Process;
using squad.Ui.Abstractions;

namespace squad.HeadquarterTools.History;

/// <summary>
/// Launches the optional `gitHistoryCommand` configured in `blaxquad/squad.json` in the workspace directory.
/// Constructed once, unconfigured, alongside every other collaborator that shares the hosting context; a later
/// <see cref="Configure"/> call - made from the validated, immutable prepared-launch result, never by re-reading
/// or re-parsing `squad.json` here - resolves the configured executable exactly once, before the window starts.
/// An omitted command, or one whose executable does not resolve, simply leaves Git history unavailable; a
/// malformed but present command is rejected up front by `SquadConfigurationLoader`, so this type never needs to
/// duplicate that validation.
/// </summary>
public sealed class GitHistoryTool : IWorkspaceTools
{
    private readonly string myWorkingDirectory;
    private string? myResolvedExecutable;
    private IReadOnlyList<string> myArguments = [];

    public GitHistoryTool(string workingDirectory)
    {
        myWorkingDirectory = workingDirectory;
    }

    public bool GitHistoryAvailable => myResolvedExecutable is not null;

    public void Configure(IReadOnlyList<string>? gitHistoryCommand)
    {
        myResolvedExecutable = gitHistoryCommand is { Count: > 0 } command
            ? ExecutableLocator.Resolve(command[0])
            : null;
        myArguments = myResolvedExecutable is null ? [] : gitHistoryCommand!.Skip(1).ToArray();
    }

    public void OpenGitHistory()
    {
        if (myResolvedExecutable is null)
        {
            throw new InvalidOperationException("Git history is not available.");
        }
        ProcessRunner.Start(myResolvedExecutable, myArguments, myWorkingDirectory);
    }
}

