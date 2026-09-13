using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using squad.Domain;

namespace squad.Configuration;

/// <summary>Loads and validates the complete launch configuration, including role prompts and safe worktree paths.
/// Only schema version 2 (a reusable "roles" name catalog plus a "members" array) is accepted; a missing version
/// or a legacy object-valued "roles" array is rejected with an explicit, identity-preserving migration diagnostic
/// rather than silently reinterpreted.</summary>
public static class SquadConfigurationLoader
{
    private const int SupportedSchemaVersion = 2;

    private static readonly JsonSerializerOptions myJsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static SquadConfiguration Load(string configFile, string rolesDirectory)
    {
        string text;

        try
        {
            text = File.ReadAllText(configFile);
        }
        catch (IOException exception)
        {
            throw Error($"could not read {configFile}: {exception.Message}");
        }

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException exception)
        {
            throw Error($"invalid JSON in {configFile}: {exception.Message}");
        }

        using (document)
        {
            RejectLegacySchema(document.RootElement, configFile);

            SquadConfigurationDocument typed;

            try
            {
                typed = JsonSerializer.Deserialize<SquadConfigurationDocument>(text, myJsonOptions)
                    ?? throw Error("configuration must be a JSON object");
            }
            catch (JsonException exception)
            {
                throw Error($"invalid JSON in {configFile}: {exception.Message}");
            }

            return Validate(typed, configFile, rolesDirectory);
        }
    }

    /// <summary>Rejects a document that omits the required "schemaVersion": 2 marker or still carries the legacy
    /// (version-1) object-valued "roles" array, with a message that documents the identity-preserving migration
    /// instead of silently dual-reading the old shape: preserve each old entry's "name" as the member's name, add
    /// that name to a new "roles" catalog, set the member's "role" to the same name, and leave "leader"
    /// unchanged.</summary>
    private static void RejectLegacySchema(JsonElement root, string configFile)
    {

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw Error("configuration must be a JSON object");
        }

        var hasCurrentSchemaVersion = root.TryGetProperty("schemaVersion", out var schemaVersionElement)
            && schemaVersionElement.ValueKind == JsonValueKind.Number
            && schemaVersionElement.TryGetInt32(out var schemaVersion)
            && schemaVersion == SupportedSchemaVersion;
        var legacyRolesShape = root.TryGetProperty("roles", out var rolesElement)
            && rolesElement.ValueKind == JsonValueKind.Array
            && rolesElement.EnumerateArray().Any(entry => entry.ValueKind == JsonValueKind.Object);

        if (hasCurrentSchemaVersion && !legacyRolesShape)
        {
            return;
        }

        throw Error(
            $"configuration {configFile} must declare \"schemaVersion\": {SupportedSchemaVersion} with a \"roles\" " +
            "array of role names and a \"members\" array of configured participants; it is not dual-read as the " +
            "legacy shape. Migrate a version-1 configuration by keeping each old entry's \"name\" as its member " +
            "name, adding that name to \"roles\", setting the member's \"role\" to the same name, moving the " +
            "entry's \"worktree\", \"receiveMode\", and \"agent\" onto that member, and leaving \"leader\" unchanged.");
    }

    private static SquadConfiguration Validate(SquadConfigurationDocument document, string configFile, string rolesDirectory)
    {
        var rootDirectory = Path.GetFullPath(Path.Combine(rolesDirectory, "..", ".."));
        var roles = ValidateRoles(document.Roles, configFile, rolesDirectory);
        var sharedWorktreePaths = ValidateSharedWorktreePaths(document.SharedWorktreePaths, rootDirectory, configFile);
        var members = ValidateMembers(document.Members, roles, configFile, rolesDirectory);
        var gitHistoryCommand = ValidateGitHistoryCommand(document.GitHistoryCommand, configFile);

        // "leader" is optional: an omitted or blank value defaults to the first configured member, so there is
        // always an authoritative leader. An explicitly configured value that does not match any member is still a
        // configuration error - a plausible typo, not "no leader configured".
        var leader = string.IsNullOrWhiteSpace(document.Leader) ? members[0].Name.Value : document.Leader;

        if (!members.Any(member => member.Name.Value == leader))
        {
            throw Error($"leader '{leader}' in {configFile} must match a configured member name");
        }

        return new SquadConfiguration(
            roles.Select(role => new RoleId(role)).ToList(), members, new SquadMemberId(leader), sharedWorktreePaths, gitHistoryCommand);
    }

    private static IReadOnlyList<string> ValidateRoles(List<string>? documentRoles, string configFile, string rolesDirectory)
    {

        if (documentRoles is null || documentRoles.Count == 0)
        {
            throw Error($"configuration {configFile} requires a non-empty roles array");
        }

        var roles = new List<string>(documentRoles.Count);
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var role in documentRoles)
        {
            var name = Required(role, "role name");

            if (!names.Add(name))
            {
                throw Error($"Duplicate role '{name}' in {configFile}");
            }

            var promptFile = Path.Combine(rolesDirectory, name + ".prompt");

            if (!File.Exists(promptFile))
            {
                throw Error($"Missing role prompt {promptFile}");
            }

            roles.Add(name);
        }

        return roles;
    }

    private static IReadOnlyList<SquadMemberConfiguration> ValidateMembers(
        List<SquadConfigurationMemberDocument>? documentMembers,
        IReadOnlyList<string> roles,
        string configFile,
        string rolesDirectory)
    {

        if (documentMembers is null || documentMembers.Count == 0)
        {
            throw Error($"configuration {configFile} requires a non-empty members array");
        }

        var roleNames = new HashSet<string>(roles, StringComparer.Ordinal);
        var members = new List<SquadMemberConfiguration>(documentMembers.Count);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var worktrees = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var masterCount = 0;
        var rootDirectory = Path.GetFullPath(Path.Combine(rolesDirectory, "..", ".."));
        var worktreesDirectory = Path.Combine(Path.GetDirectoryName(rolesDirectory)!, "..", ".worktrees");

        foreach (var member in documentMembers)
        {
            var name = Required(member.Name, "member name");
            var role = Required(member.Role, $"role for member '{name}'");
            var worktree = Required(member.Worktree, $"worktree for member '{name}'");
            var receiveMode = member.ReceiveMode ?? "task";
            var agent = member.Agent ?? throw Error($"member '{name}' requires agent");
            var permissions = agent.Permissions ?? "prompt";

            if (agent.Model is not null && string.IsNullOrWhiteSpace(agent.Model))
            {
                throw Error($"agent.model for member '{name}' cannot be empty");
            }

            if (agent.Effort is not null && string.IsNullOrWhiteSpace(agent.Effort))
            {
                throw Error($"agent.effort for member '{name}' cannot be empty");
            }

            if (name.Contains('_'))
            {
                throw Error($"Invalid member '{name}': member names may not contain underscores");
            }

            if (!names.Add(name))
            {
                throw Error($"Duplicate member '{name}' in {configFile}");
            }

            if (!roleNames.Contains(role))
            {
                throw Error($"member '{name}' references unknown role '{role}' in {configFile}");
            }

            if (worktree.Contains('/') || worktree.Contains('\\') || worktree is "." or "..")
            {
                throw Error($"Invalid worktree '{worktree}' for member '{name}'");
            }

            if (worktree != "master" && !worktrees.Add(worktree))
            {
                throw Error($"Duplicate worktree '{worktree}' in {configFile}");
            }

            if (worktree == "master" && ++masterCount > 1)
            {
                throw Error($"Duplicate worktree 'master' in {configFile}");
            }

            if (receiveMode is not ("task" or "batch"))
            {
                throw Error($"Invalid receive mode '{receiveMode}' for member '{name}': expected task or batch");
            }

            if (permissions is not ("prompt" or "approveAll"))
            {
                throw Error($"Invalid permissions '{permissions}' for member '{name}': expected prompt or approveAll");
            }

            var worktreePath = Path.GetFullPath(WorktreeTarget.Parse(worktree).ResolvePath(rootDirectory, worktreesDirectory));

            if (!paths.Add(worktreePath))
            {
                throw Error($"Duplicate normalized worktree path '{worktreePath}' in {configFile}");
            }

            var displayName = string.IsNullOrWhiteSpace(member.DisplayName) ? DisplayNameFor(name) : member.DisplayName;
            var typedReceiveMode = receiveMode == "task" ? ReceiveMode.Task : ReceiveMode.Batch;
            var permissionMode = permissions == "approveAll" ? PermissionMode.ApproveAll : PermissionMode.Prompt;
            members.Add(new SquadMemberConfiguration(new SquadMemberId(name), displayName, new RoleId(role), WorktreeTarget.Parse(worktree), typedReceiveMode,
                new AgentSettings(permissionMode, agent.Model, agent.Effort)));
        }

        return members;
    }

    // An omitted "gitHistoryCommand" is a valid "Git history unavailable" configuration, not an error: the array
    // only needs validation once it is actually configured. The first item is the executable and every remaining
    // item is one exact argument; both a missing executable and any blank item are configuration errors, matching
    // how every other configured array in this file is validated.
    private static IReadOnlyList<string>? ValidateGitHistoryCommand(List<string>? configured, string configFile)
    {

        if (configured is null)
        {
            return null;
        }

        if (configured.Count == 0 || string.IsNullOrWhiteSpace(configured[0]))
        {
            throw Error($"gitHistoryCommand in {configFile} must start with a non-blank executable");
        }

        foreach (var item in configured)
        {

            if (string.IsNullOrWhiteSpace(item))
            {
                throw Error($"gitHistoryCommand in {configFile} cannot contain a blank item");
            }

        }

        return configured;
    }

    private static IReadOnlyList<string> ValidateSharedWorktreePaths(
        List<string>? configuredPaths,
        string rootDirectory,
        string configFile)
    {

        if (configuredPaths is null)
        {
            return [];
        }

        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var paths = new List<string>(configuredPaths.Count);
        var normalizedPaths = new HashSet<string>(comparer);

        foreach (var configuredPath in configuredPaths)
        {

            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                throw Error($"sharedWorktreePaths in {configFile} cannot contain an empty path");
            }

            if (Path.IsPathFullyQualified(configuredPath))
            {
                throw Error($"Shared worktree path '{configuredPath}' in {configFile} must be relative");
            }

            var fullPath = Path.GetFullPath(Path.Combine(rootDirectory, configuredPath));

            if (!IsWithin(rootDirectory, fullPath))
            {
                throw Error($"Shared worktree path '{configuredPath}' in {configFile} must stay within the repository root");
            }

            if (!normalizedPaths.Add(fullPath))
            {
                throw Error($"Duplicate shared worktree path '{configuredPath}' in {configFile}");
            }

            if (normalizedPaths.Any(path => path != fullPath && (IsWithin(path, fullPath) || IsWithin(fullPath, path))))
            {
                throw Error($"Overlapping shared worktree path '{configuredPath}' in {configFile}");
            }

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

    private static string DisplayNameFor(string name) =>
        string.Join(" ", Regex.Split(Regex.Replace(name, "[-_]", " "), @"\s+")
            .Where(part => part.Length > 0)
            .Select(part => part.Length == 1 ? part.ToUpperInvariant() : char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));

    private static SquadConfigurationException Error(string message) => new(message);
}
