namespace squad.Handoff;

/// <summary>Reads/writes the "field: value" header block that precedes the blank-line body separator in a handoff file.</summary>
public static class HandoffHeaders
{
    public static string? HeaderField(string filePath, string field)
    {
        var prefix = field + ": ";
        foreach (var line in File.ReadLines(filePath))
        {
            if (string.IsNullOrWhiteSpace(line))
                break; // header block ends at the first blank line
            if (line.StartsWith(prefix, StringComparison.Ordinal))
                return line[prefix.Length..];
        }
        return null;
    }

    public static string Body(string filePath)
    {
        var content = File.ReadAllText(filePath);
        var (separator, length) = FindHeaderBodySeparator(content);
        return separator >= 0 ? content[(separator + length)..] : "";
    }

    /// <summary>Splits a handoff into header block and body, accepting LF or CRLF blank-line separators.</summary>
    public static (string Header, string Body) SplitMessage(string content)
    {
        var (separator, length) = FindHeaderBodySeparator(content);
        return separator >= 0
            ? (content[..separator], content[(separator + length)..])
            : (content, "");
    }

    public static (int Index, int Length) FindHeaderBodySeparator(string content)
    {
        var crlf = content.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var lf = content.IndexOf("\n\n", StringComparison.Ordinal);
        if (crlf < 0) return (lf, 2);
        if (lf < 0) return (crlf, 4);
        return crlf < lf ? (crlf, 4) : (lf, 2);
    }

    /// <summary>Replaces the header if present (before the first blank line), otherwise inserts it right before the blank line.</summary>
    public static void SetHeader(string filePath, string field, string value)
    {
        var prefix = field + ": ";
        var lines = File.ReadAllLines(filePath);
        var result = new List<string>(lines.Length + 1);
        var inserted = false;
        var replaced = false;

        foreach (var line in lines)
        {
            if (!inserted && string.IsNullOrWhiteSpace(line))
            {
                if (!replaced)
                    result.Add(prefix + value);
                result.Add(line);
                inserted = true;
            }
            else if (!inserted && line.StartsWith(prefix, StringComparison.Ordinal))
            {
                result.Add(prefix + value);
                replaced = true;
            }
            else
            {
                result.Add(line);
            }
        }

        if (!inserted && !replaced)
            result.Add(prefix + value);

        var directory = Path.GetDirectoryName(filePath)!;
        var tmp = Path.Combine(directory, $".headers.{Guid.NewGuid():N}");
        File.WriteAllText(tmp, string.Join("\n", result) + "\n");
        File.Move(tmp, filePath, overwrite: true);
    }
}



