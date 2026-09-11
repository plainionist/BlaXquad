using System.Text.Json;
using squad.Process;
using squad.Ui.Abstractions;

namespace squad.Tools.History;

/// <summary>
/// Resolves the optional `gitHistoryCommand` configured in `blaxquad/squad.json` and launches it in the workspace
/// directory. A missing configuration file, a missing or unresolvable command, or any other read failure simply
/// leaves Git history unavailable - a malformed but present command is still rejected up front by
/// <c>SquadConfigurationLoader</c>, so this narrow reader never needs to duplicate that validation.
/// </summary>
public sealed class GitHistoryTool : IWorkspaceTools
{
    private readonly string myWorkingDirectory;
    private readonly string? myExecutable;
    private readonly IReadOnlyList<string> myArguments;

    private GitHistoryTool(string workingDirectory, string? executable, IReadOnlyList<string> arguments)
    {
        myWorkingDirectory = workingDirectory;
        myExecutable = executable;
        myArguments = arguments;
    }

    public bool GitHistoryAvailable => myExecutable is not null;

    /// <summary>Reads `gitHistoryCommand` directly from the given `squad.json`, resolving its executable against
    /// `PATH`/`PATHEXT` (or an explicitly qualified path). Never throws: any problem reading or resolving the
    /// command results in Git history being reported unavailable.</summary>
    public static GitHistoryTool Resolve(string workingDirectory, string configFile)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configFile));
            if (!document.RootElement.TryGetProperty("gitHistoryCommand", out var commandElement)
                || commandElement.ValueKind != JsonValueKind.Array)
            {
                return new GitHistoryTool(workingDirectory, null, []);
            }

            var command = commandElement.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item!)
                .ToArray();
            if (command.Length == 0 || !ExecutableLocator.Exists(command[0]))
            {
                return new GitHistoryTool(workingDirectory, null, []);
            }

            return new GitHistoryTool(workingDirectory, command[0], command.Skip(1).ToArray());
        }
        catch
        {
            return new GitHistoryTool(workingDirectory, null, []);
        }
    }

    public void OpenGitHistory()
    {
        if (myExecutable is null)
        {
            throw new InvalidOperationException("Git history is not available.");
        }
        ProcessRunner.Start(myExecutable, myArguments, myWorkingDirectory);
    }
}
