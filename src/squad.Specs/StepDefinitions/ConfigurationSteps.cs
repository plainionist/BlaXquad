using global::squad.Specs.Support;
using global::squad.Agent;

namespace squad.Specs.StepDefinitions;

[Binding]
public sealed class ConfigurationSteps
{
    private readonly ScenarioWorkspace myWorkspace;

    public ConfigurationSteps(ScenarioWorkspace workspace)
    {
        myWorkspace = workspace;
    }

    [Given("a constitution prompt exists")]
    public void GivenAConstitutionPromptExists()
    {
        myWorkspace.WriteFile("blaxquad/constitution.prompt", "Follow the project constitution.\n");
    }

    [Given("this squad configuration:")]
    public void GivenThisSquadConfiguration(string configuration)
    {
        myWorkspace.WriteFile("blaxquad/squad.json", configuration + "\n");
    }

    [Given("role prompts exist for {string}")]
    public void GivenRolePromptsExistFor(string commaSeparatedRoles)
    {
        foreach (var role in commaSeparatedRoles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            myWorkspace.WriteFile($"blaxquad/roles/{role}.prompt", $"Act as the {role}.\n");
    }

    [When("the squad configuration is parsed")]
    public void WhenTheSquadConfigurationIsParsed()
    {
        myWorkspace.RunTool("squad-hq", ["launch", "--test-parse", myWorkspace.Root]);
    }

    [When("startup state is prepared")]
    public void WhenStartupStateIsPrepared() =>
        myWorkspace.RunTool("squad-hq", ["launch", "--test-prepare-launch", myWorkspace.Root]);

    [Given("the configured {string} worktree has local changes and queued handoffs")]
    public void GivenTheConfiguredWorktreeHasLocalChangesAndQueuedHandoffs(string role)
    {
        myWorkspace.RunTool("squad-hq", ["launch", "--test-prepare-launch", myWorkspace.Root]);
        var worktree = myWorkspace.PathInWorkspace(".worktrees", role);
        File.WriteAllText(Path.Combine(worktree, "blaxquad", "roles", role + ".prompt"), "Uncommitted worktree change\n");
        foreach (var directory in new[] { "outbox", "sent", "failed", "inbox/new", "inbox/in_process", "inbox/completed" })
        {
            var handoff = Path.Combine(worktree, ".blaxquad", "handoffs", directory, "pending.handoff");
            Directory.CreateDirectory(Path.GetDirectoryName(handoff)!);
            File.WriteAllText(handoff, "pending\n");
        }
        var batch = Path.Combine(worktree, ".blaxquad", "handoffs", "inbox", "new", "batch_pending");
        Directory.CreateDirectory(batch);
        File.WriteAllText(Path.Combine(batch, "pending.handoff"), "pending\n");
    }

    [Given("the configured {string} worktree has an empty replacement for shared path {string}")]
    public void GivenTheConfiguredWorktreeHasAnEmptyReplacementForSharedPath(string role, string sharedPath)
    {
        myWorkspace.RunTool("squad-hq", ["launch", "--test-prepare-launch", myWorkspace.Root]);
        var replacement = myWorkspace.PathInWorkspace(".worktrees", role, sharedPath);
        if (Path.Exists(replacement))
            Directory.Delete(replacement);
        Directory.CreateDirectory(replacement);
    }

    [When("launch preparation runs")]
    public void WhenLaunchPreparationRuns()
    {
        var result = myWorkspace.RunTool("squad-hq", ["launch", "--test-prepare-launch", myWorkspace.Root]);
        Assert.That(result.ExitCode, Is.Zero, result.StdErr);
    }

    [When("launch preparation continues")]
    public void WhenLaunchPreparationContinues() =>
        myWorkspace.RunTool("squad-hq", ["launch", "--test-continue-launch", myWorkspace.Root]);

    [Then("runtime role {string} uses worktree {string} and receive mode {string}")]
    public void ThenRuntimeRoleUsesWorktreeAndReceiveMode(string role, string worktree, string receiveMode)
    {
        var roles = SquadConfig.ReadRoles(myWorkspace.Root);
        var match = roles.SingleOrDefault(r => r.Role == role);
        Assert.That(match, Is.Not.Null, $"Role '{role}' not found in configuration.");

        Assert.Multiple(() =>
        {
            Assert.That(match!.WorktreeName, Is.EqualTo(worktree));
            Assert.That(match!.ReceiveMode, Is.EqualTo(receiveMode));
        });
    }

    [Then("the configured {string} worktree has no local changes")]
    public void ThenTheConfiguredWorktreeHasNoLocalChanges(string role) =>
        Assert.That(File.ReadAllText(myWorkspace.PathInWorkspace(".worktrees", role, "blaxquad", "roles", role + ".prompt")), Is.EqualTo($"Act as the {role}.{Environment.NewLine}"));

    [Then("the configured {string} worktree retains local changes")]
    public void ThenTheConfiguredWorktreeRetainsLocalChanges(string role) =>
        Assert.That(File.ReadAllText(myWorkspace.PathInWorkspace(".worktrees", role, "blaxquad", "roles", role + ".prompt")), Is.EqualTo("Uncommitted worktree change\n"));

    [Then("the configured {string} worktree has no queued handoffs")]
    public void ThenTheConfiguredWorktreeHasNoQueuedHandoffs(string role)
    {
        var handoffs = myWorkspace.PathInWorkspace(".worktrees", role, ".blaxquad", "handoffs");
        foreach (var directory in new[] { "outbox", "sent", "failed", "inbox/new", "inbox/in_process", "inbox/completed" })
            Assert.That(Directory.EnumerateFiles(Path.Combine(handoffs, directory), "*.handoff"), Is.Empty);
        Assert.That(Directory.EnumerateDirectories(Path.Combine(handoffs, "inbox"), "batch_*", SearchOption.AllDirectories), Is.Empty);
    }

    [Then("the configured {string} worktree retains queued handoffs")]
    public void ThenTheConfiguredWorktreeRetainsQueuedHandoffs(string role) =>
        Assert.That(File.Exists(myWorkspace.PathInWorkspace(".worktrees", role, ".blaxquad", "handoffs", "outbox", "pending.handoff")), Is.True);

    [Then("the configured {string} worktree shares path {string} with the root repository")]
    public void ThenTheConfiguredWorktreeSharesPathWithTheRootRepository(string role, string sharedPath)
    {
        var rootPath = myWorkspace.PathInWorkspace(sharedPath);
        var marker = Path.Combine(rootPath, "shared-marker.txt");
        File.WriteAllText(marker, "shared\n");
        Assert.That(File.ReadAllText(myWorkspace.PathInWorkspace(".worktrees", role, sharedPath, "shared-marker.txt")), Is.EqualTo("shared\n"));
    }
}



