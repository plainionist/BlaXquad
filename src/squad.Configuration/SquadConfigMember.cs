using squad.Domain;

namespace squad.Configuration;

/// <summary>One member as read by the lenient command-side configuration scan in <see cref="SquadConfig"/>:
/// only what CLI and handoff commands need to resolve the current role, validate a recipient, and report
/// receive-mode diagnostics. <see cref="ReceiveMode"/> is <c>null</c> when the configured token is explicitly
/// empty or unsupported; <see cref="RawReceiveMode"/> carries the literal configured token (defaulting to
/// <c>"task"</c> when omitted) so a command can still distinguish an empty token from an unsupported one without
/// re-scanning the configuration file.</summary>
public sealed record SquadConfigMember(
    SquadMemberId Id,
    string WorktreePath,
    ReceiveMode? ReceiveMode,
    string RawReceiveMode);
