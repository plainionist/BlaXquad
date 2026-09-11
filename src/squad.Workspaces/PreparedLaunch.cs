using squad.AgentProvider.Abstractions;
using squad.Configuration;

namespace squad.Workspaces;

/// <summary>
/// The immutable result of one launch-preparation pipeline. Every collaborator constructed after preparation -
/// the agent backend, the session view model, and handoff delivery - is built directly from these values; none of
/// them may reach back into the mutable discovery state used to produce them.
/// </summary>
public sealed record PreparedLaunch(
    AgentBackendContext BackendContext,
    IReadOnlyList<string> RoleNames,
    string Leader,
    IReadOnlyList<RoleRow> HandoffRoles,
    string HandoffLogPath,
    IReadOnlyList<string>? GitHistoryCommand);
