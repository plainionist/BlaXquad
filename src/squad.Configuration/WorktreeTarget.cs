namespace squad.Configuration;

/// <summary>The configured worktree target for one squad member: either the shared project-root worktree or one
/// named linked worktree created under <c>.worktrees</c>. Represents the parsed "worktree" configuration value so
/// callers no longer compare against the documented <c>"master"</c> sentinel themselves.</summary>
public sealed record WorktreeTarget
{
    /// <summary>The project-root worktree - the main checkout itself.</summary>
    public static readonly WorktreeTarget ProjectRoot = new((string?)null);

    /// <summary>The linked worktree's configured name, or <c>null</c> for <see cref="ProjectRoot"/>.</summary>
    public string? Name { get; }

    public bool IsProjectRoot => Name is null;

    private WorktreeTarget(string? name) => Name = name;

    /// <summary>Parses the raw configured "worktree" token: the documented <c>"master"</c> value is the project
    /// root; any other name is a linked worktree, which must be nonblank, not <c>"."</c> or <c>".."</c>, and free
    /// of path separators, without normalizing the supplied name.</summary>
    public static WorktreeTarget Parse(string raw)
    {

        if (raw == "master")
        {
            return ProjectRoot;
        }

        Contract.Requires(!string.IsNullOrWhiteSpace(raw), "worktree target must not be null or blank.");
        Contract.Requires(raw is not ("." or ".."), $"worktree target '{raw}' must not be '.' or '..'.");
        Contract.Requires(!raw.Contains('/') && !raw.Contains('\\'), $"worktree target '{raw}' must not contain a path separator.");

        return new WorktreeTarget(raw);
    }

    /// <summary>Resolves this target to its concrete worktree path: <paramref name="workingDir"/> itself for the
    /// project root, or the linked worktree's directory under <paramref name="worktreesDir"/> otherwise.</summary>
    public string ResolvePath(string workingDir, string worktreesDir) =>
        IsProjectRoot ? workingDir : Path.Combine(worktreesDir, Name!);
}
