namespace squad.Application.Members;

/// <summary>
/// One configured squad member's unique identity, presentation, and referenced role, supplied once when the
/// member directory is initialized. Not exposed at any public wire boundary.
/// </summary>
public sealed record MemberConfiguration(string Member, string DisplayName, string Role);
