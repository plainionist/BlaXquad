using squad.Ui.Abstractions;

namespace squad.Ui.Protocol;

/// <summary>Maps the authoritative issue catalog to the wire payload shape consumed by the UI.</summary>
internal static class IssueProtocol
{
    public static object CreateListPayload(IReadOnlyList<IssueDescriptor> issues) => new
    {
        issues = issues.Select(issue => new
        {
            path = issue.Path,
            title = issue.Title,
            priority = issue.Priority,
            frontmatter = issue.Frontmatter,
            previewLines = issue.PreviewLines,
        }),
    };
}
