using System.Globalization;
using System.Text.RegularExpressions;

namespace squad.Handoffs;

/// <summary>Validates the fixed-width priority representation used for lexically ordered handoff filenames and
/// converts between that CLI-facing text and the native integer priority carried by a handoff document.</summary>
public static class Priority
{
    private static readonly Regex myTwoDigits = new("^[0-9]{2}$", RegexOptions.Compiled);

    /// <summary>Accepts exactly two decimal digits, from <c>00</c> through <c>99</c>.</summary>
    public static bool IsValid(string? value) => value is not null && myTwoDigits.IsMatch(value);

    /// <summary>Parses an already-validated two-digit priority into its native integer value.</summary>
    public static int Parse(string value)
    {
        Contract.Requires(IsValid(value), "value must be exactly two decimal digits.");
        return int.Parse(value, CultureInfo.InvariantCulture);
    }

    /// <summary>Formats a native integer priority as two digits for filename ordering or CLI presentation.</summary>
    public static string Format(int priority)
    {
        Contract.Requires(priority is >= 0 and <= 99, "priority must be from 0 through 99.");
        return priority.ToString("D2", CultureInfo.InvariantCulture);
    }
}


