using System.Text.RegularExpressions;

namespace squad.Handoffs;

/// <summary>The canonical identity of one Git commit as this codebase spells it everywhere: exactly ten lowercase
/// hexadecimal characters, the same abbreviation `git rev-parse --short=10` produces. A sealed reference type, not
/// a struct, so no default-constructed instance can ever bypass its constructor invariant. Rejects any value that
/// is not exactly ten lowercase hex characters at construction; does not normalize supplied text.</summary>
public sealed record GitCommitId
{
    private static readonly Regex CanonicalForm = new("^[0-9a-f]{10}$", RegexOptions.Compiled);

    public string Value { get; }

    public GitCommitId(string value)
    {
        Contract.Requires(!string.IsNullOrWhiteSpace(value), "value must not be null or blank.");
        Contract.Requires(CanonicalForm.IsMatch(value), "value must be exactly ten lowercase hexadecimal characters.");

        Value = value;
    }

    public override string ToString() => Value;
}
