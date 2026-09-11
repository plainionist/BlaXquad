namespace squad.Tools.Issues;

/// <summary>Normalizes an absolute issue file path into a workspace-relative path using '/' separators.</summary>
internal static class IssuePathFormatter
{
    public static string ToWorkspaceRelativePath(string workspaceRoot, string filePath) =>
        Path.GetRelativePath(workspaceRoot, filePath)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
}
