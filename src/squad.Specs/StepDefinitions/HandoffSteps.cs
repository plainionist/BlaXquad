using global::squad.Specs.Support;

namespace squad.Specs.StepDefinitions;

[Binding]
public sealed class HandoffSteps
{
    private const string DraftKey = "draft";
    private readonly ScenarioWorkspace myWorkspace;

    public HandoffSteps(ScenarioWorkspace workspace)
    {
        myWorkspace = workspace;
    }

    [Given("a Git project with roles {string}")]
    public void GivenAGitProjectWithRoles(string commaSeparatedRoles)
    {
        myWorkspace.InitializeGitRepository();
        var roles = commaSeparatedRoles
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var worktrees = roles.ToDictionary(
            role => role,
            role => role == roles[0]
                ? myWorkspace.Root
                : myWorkspace.PathInWorkspace(".worktrees", role),
            StringComparer.Ordinal);

        foreach (var role in roles.Skip(1))
        {
            Assert.That(
                myWorkspace.RunGit("worktree", "add", "--quiet", "-b", $"squad-{role}", worktrees[role]).ExitCode,
                Is.Zero);
        }

        var rolesJson = string.Join(",\n", roles.Select((role, index) =>
            $"{{ \"name\": \"{role}\", \"worktree\": \"{(index == 0 ? "master" : role)}\", \"agent\": {{}} }}"));
        myWorkspace.WriteFile("blaxquad/squad.json", $"{{\n  \"roles\": [\n{rolesJson}\n  ]\n}}\n");
        myWorkspace.WriteFile("blaxquad/constitution.prompt", "Follow constitution.\n");
        foreach (var role in roles)
            myWorkspace.WriteFile($"blaxquad/roles/{role}.prompt", $"Act as {role}.\n");
    }

    [Given("{string} has a committed change")]
    public void GivenRoleHasACommittedChange(string role)
    {
        myWorkspace.WriteFile($"{role}-change.txt", "completed\n");
        Assert.That(myWorkspace.RunGit("add", $"{role}-change.txt").ExitCode, Is.Zero);
        Assert.That(myWorkspace.RunGit("commit", "--quiet", "-m", $"{role} change").ExitCode, Is.Zero);
        var result = myWorkspace.RunGit("rev-parse", "--short=10", "HEAD");
        Assert.That(result.ExitCode, Is.Zero);
        myWorkspace.Set("commit", result.StdOut.Trim());
    }

    [Given("{string} prepares a Git handoff to {string} with priority {string} and task {string}")]
    public void GivenRolePreparesAGitHandoff(string role, string recipient, string priority, string task)
    {
        WriteDraft(
            $"type: git_handoff\nto: {recipient}\npriority: {priority}\ntask: {task}\ncommit: {myWorkspace.Get<string>("commit")}\n");
    }

    [Given("{string} prepares a note to {string} with priority {string} and message {string}")]
    public void GivenRolePreparesANote(string role, string recipients, string priority, string message)
    {
        WriteDraft($"type: note\nto: {recipients}\npriority: {priority}\nmessage: {message}\n");
    }

    [Given("{string} prepares this handoff draft:")]
    public void GivenRolePreparesThisHandoffDraft(string role, string draft)
    {
        WriteDraft(draft + "\n");
    }

    [When("{string} queues the handoff")]
    public void WhenRoleQueuesTheHandoff(string role)
    {
        myWorkspace.RunTool("squad", ["handoff", myWorkspace.Get<string>(DraftKey)]);
    }

    [Then("the draft is removed")]
    public void ThenTheDraftIsRemoved() =>
        Assert.That(File.Exists(myWorkspace.Get<string>(DraftKey)), Is.False);

    [Then("the draft remains")]
    public void ThenTheDraftRemains() =>
        Assert.That(File.Exists(myWorkspace.Get<string>(DraftKey)), Is.True);

    [Then("one handoff is queued")]
    public void ThenOneHandoffIsQueued() =>
        Assert.That(QueuedHandoffs(), Has.Length.EqualTo(1));

    [Then("no handoff is queued")]
    public void ThenNoHandoffIsQueued() =>
        Assert.That(QueuedHandoffs(), Is.Empty);

    [Then("the queued handoff has header {string} with value {string}")]
    public void ThenTheQueuedHandoffHasHeader(string field, string expected)
    {
        var headers = ReadHandoff().Split("\n\n", 2)[0].Split('\n');
        Assert.That(headers, Does.Contain($"{field}: {expected}"));
    }

    [Then("the queued handoff payload starts with {string}")]
    public void ThenTheQueuedHandoffPayloadStartsWith(string expected) =>
        Assert.That(HandoffBody(), Does.StartWith(expected));

    [Then("the queued handoff payload is {string}")]
    public void ThenTheQueuedHandoffPayloadIs(string expected) =>
        Assert.That(HandoffBody().TrimEnd(), Is.EqualTo(expected));

    private void WriteDraft(string content)
    {
        var path = myWorkspace.PathInWorkspace("handoff-draft.txt");
        myWorkspace.WriteFile("handoff-draft.txt", content);
        myWorkspace.Set(DraftKey, path);
    }

    private string[] QueuedHandoffs()
    {
        var outbox = myWorkspace.PathInWorkspace(".blaxquad", "handoffs", "outbox");
        return Directory.Exists(outbox)
            ? Directory.GetFiles(outbox, "*.handoff", SearchOption.TopDirectoryOnly)
            : [];
    }

    private string ReadHandoff() =>
        File.ReadAllText(QueuedHandoffs().Single()).Replace("\r\n", "\n");

    private string HandoffBody() => ReadHandoff().Split("\n\n", 2)[1];
}



