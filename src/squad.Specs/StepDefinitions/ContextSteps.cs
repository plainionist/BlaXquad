using global::squad.Specs.Support;
using System.Text.Json;

namespace squad.Specs.StepDefinitions;

[Binding]
public sealed class ContextSteps
{
    private readonly ScenarioWorkspace myWorkspace;
    private readonly Dictionary<string, string> myWorktrees = new(StringComparer.Ordinal);

    public ContextSteps(ScenarioWorkspace workspace)
    {
        myWorkspace = workspace;
    }

    [Given("a Git project with context roles {string}")]
    public void GivenAGitProjectWithContextRoles(string commaSeparatedRoles)
    {
        myWorkspace.InitializeGitRepository();
        var roles = commaSeparatedRoles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var role in roles)
        {
            var worktree = myWorkspace.PathInWorkspace(".worktrees", role);
            Assert.That(myWorkspace.RunGit("worktree", "add", "--quiet", "-b", $"squad-{role}", worktree).ExitCode, Is.Zero);
            myWorktrees.Add(role, worktree);
        }

        var rolesJson = string.Join(",\n", roles.Select(role =>
            $"{{ \"name\": \"{role}\", \"worktree\": \"{role}\", \"agent\": {{}} }}"));
        myWorkspace.WriteFile("blaxquad/squad.json", $"{{\n  \"roles\": [\n{rolesJson}\n  ]\n}}\n");
        myWorkspace.WriteFile("blaxquad/constitution.prompt", "Follow constitution.\n");
        foreach (var role in roles)
            myWorkspace.WriteFile($"blaxquad/roles/{role}.prompt", $"Act as {role}.\n");
    }

    [When("the {string} worktree queries its role context without a legacy role environment variable")]
    public void WhenTheWorktreeQueriesItsRoleContextWithoutALegacyRoleEnvironmentVariable(string role) =>
        myWorkspace.RunTool(
            "squad",
            ["context", "--field", "role"],
            workingDirectory: myWorktrees[role]);

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
        myWorkspace.RunTool(
            "squad",
            ["context", "--json"],
            new Dictionary<string, string?> { ["BLAXQUAD_SRC"] = myWorkspace.RepositoryRootPath },
            myWorktrees[role]);

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
            Assert.That(root.GetProperty("roleWorktreeRoot").GetString(), Is.EqualTo(myWorktrees[role]));
            Assert.That(root.GetProperty("sharedSourcePath").GetString(), Is.EqualTo(myWorkspace.RepositoryRootPath));
        });
    }
}



