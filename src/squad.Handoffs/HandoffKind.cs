using System.Text.Json.Serialization;

namespace squad.Handoffs;

/// <summary>Distinguishes which kind-specific payload a <see cref="HandoffDocument"/> carries.</summary>
public enum HandoffKind
{
    [JsonStringEnumMemberName("git_handoff")]
    GitHandoff,

    [JsonStringEnumMemberName("note")]
    Note,
}
