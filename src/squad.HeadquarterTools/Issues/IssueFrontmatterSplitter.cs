namespace squad.HeadquarterTools.Issues;

/// <summary>
/// Splits one issue file's raw text into its optional frontmatter block and remaining body. A frontmatter block is
/// recognized only when the first line is exactly "---"; the next line that is exactly "---" closes it. The
/// returned frontmatter block includes both delimiters, with line endings normalized to "\n". An opening delimiter
/// without a matching closing delimiter retains the whole remaining text as partial frontmatter and yields no body.
/// </summary>
internal static class IssueFrontmatterSplitter
{
    private const string Delimiter = "---";

    public static IssueFrontmatterSplit Split(string text)
    {
        var normalized = Normalize(text);
        var lines = normalized.Split('\n');

        if (lines.Length == 0 || lines[0] != Delimiter)
        {
            return new IssueFrontmatterSplit(string.Empty, string.Empty, normalized, true);
        }

        var closingIndex = Array.FindIndex(lines, 1, line => line == Delimiter);

        if (closingIndex < 0)
        {
            return new IssueFrontmatterSplit(normalized, string.Empty, string.Empty, false);
        }

        var frontmatter = string.Join('\n', lines[..(closingIndex + 1)]);
        var yamlContent = string.Join('\n', lines[1..closingIndex]);
        var body = string.Join('\n', lines[(closingIndex + 1)..]);
        return new IssueFrontmatterSplit(frontmatter, yamlContent, body, true);
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n").Replace("\r", "\n");
}
