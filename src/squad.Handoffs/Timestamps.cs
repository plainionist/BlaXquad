using System.Globalization;

namespace squad.Handoffs;

/// <summary>Provides the timestamp formats used by squad state and messages: the durable, round-trippable instant
/// carried by a handoff document's lifecycle fields, and the two purely textual formats (compact filename/id and
/// delivery-log prefix) that never become a domain value.</summary>
public static class Timestamps
{
    /// <summary>The current instant, for a handoff document's lifecycle timestamp fields.</summary>
    public static DateTimeOffset NowOffset() => DateTimeOffset.UtcNow;

    /// <summary>ISO-8601 instant using the 0/3/6/9 fractional-digit groups emitted by Java's ISO_INSTANT formatter,
    /// for filename and delivery-log text that never becomes a domain value.</summary>
    public static string Now() => Format(DateTimeOffset.UtcNow);

    /// <summary>Formats an instant with the same 0/3/6/9 fractional-digit groups <see cref="Now"/> produces, so a
    /// handoff document's lifecycle timestamps keep their established durable JSON spelling.</summary>
    public static string Format(DateTimeOffset instant)
    {
        var utc = instant.UtcDateTime;
        var fraction = (utc.Ticks % TimeSpan.TicksPerSecond).ToString("D7", CultureInfo.InvariantCulture) + "00";
        var digits = fraction.EndsWith("000000", StringComparison.Ordinal) ? 3
            : fraction.EndsWith("000", StringComparison.Ordinal) ? 6
            : 9;
        return utc.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)
            + (fraction.All(c => c == '0') ? "" : "." + fraction[..digits])
            + "Z";
    }

    /// <summary>Parses an already-persisted durable timestamp back into its instant. Accepts any valid ISO-8601
    /// instant, not only the exact fractional-digit grouping <see cref="Format"/> produces.</summary>
    public static DateTimeOffset Parse(string text)
    {
        Contract.Requires(
            DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value),
            "text must be a valid ISO-8601 instant.");
        return value;
    }

    /// <summary>Compact id/filename timestamp, e.g. "20260822T123456Z".</summary>
    public static string IdNow() =>
        DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
}

