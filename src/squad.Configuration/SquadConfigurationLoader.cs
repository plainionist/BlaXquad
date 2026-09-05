using System.Text.Json;
using System.Text.Json.Serialization;

namespace squad.Configuration;

public static class SquadConfigurationLoader
{
    private static readonly JsonSerializerOptions myJsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static SquadConfiguration Load(string configFile, string rolesDirectory)
    {
        try
        {
            var document = JsonSerializer.Deserialize<SquadConfigurationDocument>(File.ReadAllText(configFile), myJsonOptions)
                ?? throw Error("configuration must be a JSON object");
            return Validate(document, configFile, rolesDirectory);
        }
        catch (SquadConfigurationException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw Error($"invalid JSON in {configFile}: {exception.Message}");
        }
        catch (IOException exception)
        {
            throw Error($"could not read {configFile}: {exception.Message}");
        }
    }

    private static SquadConfiguration Validate(SquadConfigurationDocument document, string configFile, string rolesDirectory)
    {
        if (document.Roles is null || document.Roles.Count == 0)
            throw Error($"configuration {configFile} requires a non-empty roles array");

        var rootDirectory = Path.GetFullPath(Path.Combine(rolesDirectory, "..", ".."));
        var sharedWorktreePaths = ValidateSharedWorktreePaths(document.SharedWorktreePaths, rootDirectory, configFile);
        var roles = new List<SquadRoleConfiguration>(document.Roles.Count);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var worktrees = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var masterCount = 0;

        foreach (var role in document.Roles)
        {
            var name = Required(role.Name, "role name");
            var worktree = Required(role.Worktree, $"worktree for role '{name}'");
            var receiveMode = role.ReceiveMode ?? "task";
            var agent = role.Agent ?? throw Error($"role '{name}' requires agent");
            var permissions = agent.Permissions ?? "prompt";

            if (agent.Model is not null && string.IsNullOrWhiteSpace(agent.Model))
                throw Error($"agent.model for role '{name}' cannot be empty");
            if (agent.Effort is not null && string.IsNullOrWhiteSpace(agent.Effort))
                throw Error($"agent.effort for role '{name}' cannot be empty");

            if (name.Contains('_'))
                throw Error($"Invalid role '{name}': role names may not contain underscores");
            if (!names.Add(name))
                throw Error($"Duplicate role '{name}' in {configFile}");
            if (worktree.Contains('/') || worktree.Contains('\\') || worktree is "." or "..")
                throw Error($"Invalid worktree '{worktree}' for role '{name}'");
            if (worktree != "master" && !worktrees.Add(worktree))
                throw Error($"Duplicate worktree '{worktree}' in {configFile}");
            if (worktree == "master" && ++masterCount > 1)
                throw Error($"Duplicate worktree 'master' in {configFile}");
            if (receiveMode is not ("task" or "batch"))
                throw Error($"Invalid receive mode '{receiveMode}' for role '{name}': expected task or batch");
            if (permissions is not ("prompt" or "approveAll"))
                throw Error($"Invalid permissions '{permissions}' for role '{name}': expected prompt or approveAll");

            var promptFile = Path.Combine(rolesDirectory, name + ".prompt");
            if (!File.Exists(promptFile))
                throw Error($"Missing role prompt {promptFile}");

            var worktreePath = worktree == "master"
                ? Path.GetFullPath(Path.Combine(rolesDirectory, "..", ".."))
                : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(rolesDirectory)!, "..", ".worktrees", worktree));
            if (!paths.Add(worktreePath))
                throw Error($"Duplicate normalized worktree path '{worktreePath}' in {configFile}");

            roles.Add(new SquadRoleConfiguration(name, worktree, receiveMode,
                new SquadAgentConfiguration(permissions, agent.Model, agent.Effort)));
        }

        return new SquadConfiguration(roles, sharedWorktreePaths);
    }

    private static IReadOnlyList<string> ValidateSharedWorktreePaths(
        List<string>? configuredPaths,
        string rootDirectory,
        string configFile)
    {
        if (configuredPaths is null)
            return [];

        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var paths = new List<string>(configuredPaths.Count);
        var normalizedPaths = new HashSet<string>(comparer);
        foreach (var configuredPath in configuredPaths)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
                throw Error($"sharedWorktreePaths in {configFile} cannot contain an empty path");
            if (Path.IsPathFullyQualified(configuredPath))
                throw Error($"Shared worktree path '{configuredPath}' in {configFile} must be relative");

            var fullPath = Path.GetFullPath(Path.Combine(rootDirectory, configuredPath));
            if (!IsWithin(rootDirectory, fullPath))
                throw Error($"Shared worktree path '{configuredPath}' in {configFile} must stay within the repository root");
            if (!normalizedPaths.Add(fullPath))
                throw Error($"Duplicate shared worktree path '{configuredPath}' in {configFile}");
            if (normalizedPaths.Any(path => path != fullPath && (IsWithin(path, fullPath) || IsWithin(fullPath, path))))
                throw Error($"Overlapping shared worktree path '{configuredPath}' in {configFile}");

            paths.Add(configuredPath);
        }

        return paths;
    }

    private static bool IsWithin(string rootDirectory, string path)
    {
        var relativePath = Path.GetRelativePath(rootDirectory, path);
        return relativePath != ".." && !relativePath.StartsWith(".." + Path.DirectorySeparatorChar) && !Path.IsPathFullyQualified(relativePath);
    }

    private static string Required(string? value, string field) => string.IsNullOrWhiteSpace(value)
        ? throw Error($"{field} is required and cannot be empty")
        : value;

    private static SquadConfigurationException Error(string message) => new(message);

}



