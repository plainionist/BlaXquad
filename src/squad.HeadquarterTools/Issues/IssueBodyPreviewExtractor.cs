namespace squad.HeadquarterTools.Issues;

/// <summary>Extracts the first ten lines of an issue's body, preserving each returned line's exact text.</summary>
internal static class IssueBodyPreviewExtractor
{
    private const int MaxPreviewLines = 10;

    public static IReadOnlyList<string> ExtractPreview(string body) =>
        body
            .Split('\n')
            .Take(MaxPreviewLines)
            .ToList();
}
