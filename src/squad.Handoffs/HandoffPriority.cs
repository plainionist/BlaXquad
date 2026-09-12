using System.Globalization;
using System.Text.RegularExpressions;

namespace squad.Handoffs;

/// <summary>The priority of one handoff, ordering queued handoffs from <c>00</c> (highest) through <c>99</c>
/// (lowest) in both filename and JSON form. A readonly record struct is permitted here - unlike an identity value
/// object, its default value (priority 0) is itself a valid priority, so no reference-type rewrite is needed. Any
/// explicit construction still enforces the inclusive 0..99 range via <see cref="Contract.Requires"/>.</summary>
public readonly record struct HandoffPriority
{
    private static readonly Regex TwoDigits = new("^[0-9]{2}$", RegexOptions.Compiled);

    public int Value { get; }

    public HandoffPriority(int value)
    {
        Contract.Requires(value is >= 0 and <= 99, "value must be from 0 through 99.");
        Value = value;
    }

    /// <summary>Accepts exactly two decimal digits, from <c>00</c> through <c>99</c> - the raw CLI/filename
    /// representation, validated before it is ever parsed into a priority.</summary>
    public static bool IsValid(string? text) => text is not null && TwoDigits.IsMatch(text);

    /// <summary>Parses an already-validated two-digit priority into its native value.</summary>
    public static HandoffPriority Parse(string text)
    {
        Contract.Requires(IsValid(text), "text must be exactly two decimal digits.");
        return new HandoffPriority(int.Parse(text, CultureInfo.InvariantCulture));
    }

    /// <summary>Formats this priority as two digits for filename ordering or CLI presentation.</summary>
    public override string ToString() => Value.ToString("D2", CultureInfo.InvariantCulture);
}
