using System.Text.RegularExpressions;

namespace squad.Handoffs;

/// <summary>Validates the fixed-width priority representation used for lexically ordered handoff files.</summary>
public static class Priority
{
    private static readonly Regex myTwoDigits = new("^[0-9]{2}$", RegexOptions.Compiled);

    /// <summary>Accepts exactly two decimal digits, from <c>00</c> through <c>99</c>.</summary>
    public static bool IsValid(string? value) => value is not null && myTwoDigits.IsMatch(value);
}


