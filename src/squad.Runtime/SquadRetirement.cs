namespace squad.Runtime;

/// <summary>
/// The explicit result of retiring one squad generation. Retirement is conclusive only when every generation
/// resource was genuinely released; otherwise the squad retains the handles whose termination is uncertain, stays
/// owned by Headquarters, and blocks any replacement until a later retry succeeds.
/// </summary>
internal sealed record SquadRetirement(bool IsConclusive, IReadOnlyList<Exception> Failures)
{
    internal static SquadRetirement Conclusive { get; } = new(true, []);
}
