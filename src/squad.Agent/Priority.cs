using System.Text.RegularExpressions;

namespace squad.Agent;

public static class Priority
{
    private static readonly Regex myTwoDigits = new("^[0-9]{2}$", RegexOptions.Compiled);

    public static bool IsValid(string? value) => value is not null && myTwoDigits.IsMatch(value);
}



