using System.Globalization;

namespace squad.Agent;

/// <summary>Provides the two timestamp formats used by squad state and messages.</summary>
public static class Timestamps
{
    /// <summary>ISO-8601 instant using the 0/3/6/9 fractional-digit groups emitted by Java's ISO_INSTANT formatter.</summary>
    public static string Now()
    {
        var now = DateTime.UtcNow;
        var fraction = (now.Ticks % TimeSpan.TicksPerSecond).ToString("D7", CultureInfo.InvariantCulture) + "00";
        var digits = fraction.EndsWith("000000", StringComparison.Ordinal) ? 3
            : fraction.EndsWith("000", StringComparison.Ordinal) ? 6
            : 9;
        return now.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)
            + (fraction.All(c => c == '0') ? "" : "." + fraction[..digits])
            + "Z";
    }

    /// <summary>Compact id/filename timestamp, e.g. "20260822T123456Z".</summary>
    public static string IdNow() =>
        DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
}



