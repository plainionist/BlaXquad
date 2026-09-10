namespace squad.Issues;

/// <summary>Extracts the first five non-blank lines of an issue's body, preserving each returned line's exact text.</summary>
internal static class IssueBodyPreviewExtractor
{
    private const int MaxPreviewLines = 5;

    public static IReadOnlyList<string> ExtractPreview(string body) =>
        body
            .Split('\n')
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Take(MaxPreviewLines)
            .ToList();
}
