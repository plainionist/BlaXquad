namespace squad.Specs.Support;

/// <summary>Test-owned, decoded shape of one issue catalog entry from an "issues.list" response - the dashboard
/// protocol's normalized path, resolved title, resolved priority (absent when not present or invalid), raw
/// frontmatter block, and bounded body preview.</summary>
public sealed record IssueDescriptorObservation(
    string Path, string Title, int? Priority, string Frontmatter, IReadOnlyList<string> PreviewLines);
