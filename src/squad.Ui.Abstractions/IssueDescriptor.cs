namespace squad.Ui.Abstractions;

/// <summary>
/// Represents one discovered issue document: its workspace-relative path, title and priority (each independently
/// resolved with a fallback when the corresponding frontmatter field is absent or invalid), the raw frontmatter
/// source block, and a bounded body preview.
/// </summary>
public sealed record IssueDescriptor(
    string Path,
    string Title,
    int? Priority,
    string Frontmatter,
    IReadOnlyList<string> PreviewLines);
