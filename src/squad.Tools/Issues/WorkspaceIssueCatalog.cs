using squad.Ui.Abstractions;

namespace squad.Tools.Issues;

/// <summary>
/// Discovers Markdown issue files directly inside the fixed workspace <c>docs/issues</c> directory and parses them
/// into deterministically ordered <see cref="IssueDescriptor"/> catalog entries. Re-enumerates and re-parses the
/// directory on every call so a caller observes edits made during a running session, instead of caching a stale
/// listing or requiring a file watcher.
/// </summary>
public sealed class WorkspaceIssueCatalog : IIssueCatalog
{
    private const string MarkdownExtension = ".md";

    private readonly string myWorkspaceRoot;
    private readonly string myIssuesDirectory;

    public WorkspaceIssueCatalog(string workspaceRoot)
    {
        myWorkspaceRoot = Path.GetFullPath(workspaceRoot);
        myIssuesDirectory = Path.Combine(myWorkspaceRoot, "docs", "issues");
    }

    public async Task<IReadOnlyList<IssueDescriptor>> ListIssuesAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(myIssuesDirectory))
        {
            if (File.Exists(myIssuesDirectory))
            {
                throw new IOException($"'{myIssuesDirectory}' must be a directory.");
            }
            return [];
        }

        var descriptors = new List<IssueDescriptor>();
        foreach (var filePath in EnumerateIssueFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            descriptors.Add(CreateDescriptor(filePath, text));
        }

        return Sort(descriptors);
    }

    /// <summary>
    /// Yields only regular, top-level Markdown files. A top-level symbolic link is followed only when its final
    /// target still resolves inside the fixed issues directory; this never recurses into subdirectories.
    /// </summary>
    private IEnumerable<string> EnumerateIssueFiles()
    {
        foreach (var entryPath in Directory.EnumerateFileSystemEntries(myIssuesDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            if (!string.Equals(Path.GetExtension(entryPath), MarkdownExtension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var attributes = File.GetAttributes(entryPath);
            if (attributes.HasFlag(FileAttributes.Directory))
            {
                continue;
            }
            if (attributes.HasFlag(FileAttributes.ReparsePoint)
                && !IsWithinIssuesDirectory(File.ResolveLinkTarget(entryPath, returnFinalTarget: true)?.FullName))
            {
                continue;
            }
            yield return entryPath;
        }
    }

    private bool IsWithinIssuesDirectory(string? fullPath)
    {
        if (fullPath is null)
        {
            return false;
        }
        var relative = Path.GetRelativePath(myIssuesDirectory, fullPath);
        return relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !Path.IsPathFullyQualified(relative);
    }

    private IssueDescriptor CreateDescriptor(string filePath, string text)
    {
        var fileName = Path.GetFileName(filePath);
        var split = IssueFrontmatterSplitter.Split(text);
        var fields = IssueFrontmatterYamlParser.Parse(split.YamlContent);
        var previewLines = split.HasBody
            ? IssueBodyPreviewExtractor.ExtractPreview(split.Body)
            : (IReadOnlyList<string>)[];
        return new IssueDescriptor(
            IssuePathFormatter.ToWorkspaceRelativePath(myWorkspaceRoot, filePath),
            fields.Title ?? fileName,
            fields.Priority,
            split.Frontmatter,
            previewLines);
    }

    /// <summary>
    /// Sorts prioritized issues ascending by priority, then by filename (ordinal-ignore-case, then ordinal as a
    /// tie-breaker); unprioritized issues sort after every prioritized issue, ordered by the same filename
    /// comparison.
    /// </summary>
    private static IReadOnlyList<IssueDescriptor> Sort(List<IssueDescriptor> descriptors) =>
        descriptors
            .OrderBy(descriptor => descriptor.Priority is null ? 1 : 0)
            .ThenBy(descriptor => descriptor.Priority ?? int.MaxValue)
            .ThenBy(descriptor => Path.GetFileName(descriptor.Path), StringComparer.OrdinalIgnoreCase)
            .ThenBy(descriptor => Path.GetFileName(descriptor.Path), StringComparer.Ordinal)
            .ToList();
}
