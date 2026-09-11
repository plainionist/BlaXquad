using squad.Configuration;

namespace squadHQ.Commands;

/// <summary>Resolves the canonical squad project root for CLI commands that infer it from the current Git checkout.</summary>
internal static class ProjectRootResolver
{
    /// <summary>Resolves the main-checkout project root that owns the live Headquarters process.</summary>
    public static string ResolveViaGit() => ProjectRoot.ResolveProjectRoot(ProjectRoot.ResolveViaGit());
}
