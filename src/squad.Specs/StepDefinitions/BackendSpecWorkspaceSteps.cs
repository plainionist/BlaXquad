using squad.Specs.Support;

namespace squad.Specs.StepDefinitions;

[Binding]
public sealed class BackendSpecWorkspaceSteps
{
    private readonly ScenarioWorkspace myWorkspace;
    private IReadOnlyDictionary<string, string> myRoleWorktrees = new Dictionary<string, string>();

    public BackendSpecWorkspaceSteps(ScenarioWorkspace workspace)
    {
        myWorkspace = workspace;
    }

    [Given("a configured project with role {string}")]
    public void GivenAConfiguredProjectWithRole(string role)
    {
        myRoleWorktrees = myWorkspace.ConfigureProject(role);
    }

    [Given("a configured project with roles {string}")]
    public void GivenAConfiguredProjectWithRoles(string commaSeparatedRoles)
    {
        var roles = commaSeparatedRoles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        myRoleWorktrees = myWorkspace.ConfigureProject(roles);
    }

    [When("the {string} role worktree requests shutdown from the backend-spec squad-hq publication")]
    public void WhenTheRoleWorktreeRequestsShutdownFromTheBackendSpecSquadHqPublication(string role) =>
        myWorkspace.RunBackendSpecSquadHq(["shutdown", myWorkspace.Root], workingDirectory: myRoleWorktrees[role]);

    [When("the {string} role worktree runs an unknown backend-spec squad-hq command")]
    public void WhenTheRoleWorktreeRunsAnUnknownBackendSpecSquadHqCommand(string role) =>
        myWorkspace.RunBackendSpecSquadHq(["not-a-real-command"], workingDirectory: myRoleWorktrees[role]);

    [Then("the backend-spec command succeeds")]
    public void ThenTheBackendSpecCommandSucceeds()
    {
        var result = myWorkspace.LastResult!;
        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, () => result.StdErr);
            Assert.That(result.Executable, Is.EqualTo(myWorkspace.BackendSpecSquadHqExecutablePath),
                "The command must have run the exact squad-tools-backend-spec squad-hq publication.");
        });
    }

    [Then("the backend-spec command fails")]
    public void ThenTheBackendSpecCommandFails() =>
        Assert.That(myWorkspace.LastResult?.ExitCode, Is.Not.Zero);

    [Then("the failed command reports its executable, arguments, and working directory")]
    public void ThenTheFailedCommandReportsFullDiagnostics()
    {
        var result = myWorkspace.LastResult!;
        Assert.Multiple(() =>
        {
            Assert.That(result.Executable, Is.EqualTo(myWorkspace.BackendSpecSquadHqExecutablePath),
                "The command must have run the exact squad-tools-backend-spec squad-hq publication.");
            Assert.That(result.Arguments, Is.EqualTo(new[] { "not-a-real-command" }));
            Assert.That(result.WorkingDirectory, Is.EqualTo(myRoleWorktrees["architect"]));
        });
    }
}
