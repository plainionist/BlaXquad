using squad.Specs.Support;
using System.Text.Json;

namespace squad.Specs.StepDefinitions;

[Binding]
public sealed class ContextSteps
{
    private readonly ScenarioWorkspace myWorkspace;

    public ContextSteps(ScenarioWorkspace workspace)
    {
        myWorkspace = workspace;
    }

    [When("the {string} worktree queries its role context without a legacy role environment variable")]
    public void WhenTheWorktreeQueriesItsRoleContextWithoutALegacyRoleEnvironmentVariable(string role) =>
        myWorkspace.RunRoleTool(role, "squad", ["context", "--field", "role"]);

    [Then("the context role is {string}")]
    public void ThenTheContextRoleIs(string role)
    {
        Assert.Multiple(() =>
        {
            Assert.That(myWorkspace.LastResult?.ExitCode, Is.Zero);
            Assert.That(myWorkspace.LastResult?.StdOut.Trim(), Is.EqualTo(role));
        });
    }

    [When("the {string} worktree queries JSON context with a shared source path")]
    public void WhenTheWorktreeQueriesJsonContextWithASharedSourcePath(string role) =>
        myWorkspace.RunRoleTool(
            role,
            "squad",
            ["context", "--json"],
            new Dictionary<string, string?> { ["BLAXQUAD_SRC"] = myWorkspace.RepositoryRootPath });

    [Then("the JSON context identifies the {string} role and its worktree")]
    public void ThenTheJsonContextIdentifiesTheRoleAndItsWorktree(string role)
    {
        Assert.That(myWorkspace.LastResult?.ExitCode, Is.Zero);
        using var document = JsonDocument.Parse(myWorkspace.LastResult!.StdOut);
        var root = document.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("role").GetString(), Is.EqualTo(role));
            Assert.That(root.GetProperty("projectRoot").GetString(), Is.EqualTo(myWorkspace.Root));
            Assert.That(root.GetProperty("roleWorktreeRoot").GetString(), Is.EqualTo(myWorkspace.PathInWorkspace(".worktrees", role)));
            Assert.That(root.GetProperty("sharedSourcePath").GetString(), Is.EqualTo(myWorkspace.RepositoryRootPath));
        });
    }
}



