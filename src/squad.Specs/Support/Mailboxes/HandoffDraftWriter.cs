using squad.Specs.Support;

namespace squad.Specs.Support.Mailboxes;

/// <summary>
/// Builds and writes the current on-disk handoff draft representation into a role's worktree. Isolating the
/// draft field layout here means a later JSON/YAML draft migration changes only this support, never feature
/// language or step definitions.
/// </summary>
public sealed class HandoffDraftWriter
{
    private const string DraftFileName = "handoff-draft.txt";
    private readonly ScenarioWorkspace myWorkspace;

    public HandoffDraftWriter(ScenarioWorkspace workspace)
    {
        myWorkspace = workspace;
    }

    internal string WriteGitHandoffDraft(string role, string recipients, string priority, string task, string commit) =>
        Write(role, $"type: git_handoff\nto: {recipients}\npriority: {priority}\ntask: {task}\ncommit: {commit}\n");

    internal string WriteNoteDraft(string role, string recipients, string priority, string message) =>
        Write(role, $"type: note\nto: {recipients}\npriority: {priority}\nmessage: {message}\n");

    internal string WriteRawDraft(string role, string content) => Write(role, content + "\n");

    private string Write(string role, string content)
    {
        myWorkspace.WriteFileInRoleWorktree(role, DraftFileName, content);
        return Path.Combine(myWorkspace.RoleWorktreePath(role), DraftFileName);
    }
}
