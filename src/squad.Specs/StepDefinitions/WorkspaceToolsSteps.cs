using squad.Specs.Support.Scenarios;

namespace squad.Specs.StepDefinitions;

/// <summary>
/// Drives the real "workspace-tools.snapshot" message and a workspace tool's own narrow "open" command through
/// the shared <see cref="BackendScenario"/> process driver, entirely by configuration field name, snapshot flag
/// name, and command name given in the feature file - never by a tool's own identity. A future workspace tool
/// (beyond the one configured Git history viewer today) proves itself through these same steps with its own
/// literals, not a bespoke step of its own.
/// </summary>
[Binding]
public sealed class WorkspaceToolsSteps
{
    private static readonly TimeSpan FileAppearsTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FileAppearsPollInterval = TimeSpan.FromMilliseconds(100);

    private readonly BackendScenario myScenario;

    public WorkspaceToolsSteps(BackendScenario scenario)
    {
        myScenario = scenario;
    }

    [Given("the project configuration declares tool command {string} as:")]
    public void GivenTheProjectConfigurationDeclaresToolCommandAs(string fieldName, Table table)
    {
        var command = table.Rows.Select(row => row["argument"]).ToArray();
        myScenario.ConfigureToolCommand(fieldName, command);
    }

    [Given("the project configuration declares tool command {string} as an empty list")]
    public void GivenTheProjectConfigurationDeclaresToolCommandAsAnEmptyList(string fieldName) =>
        myScenario.ConfigureToolCommandRaw(fieldName, "[]");

    [When("a UI-protocol client sends a {string} command with request id {string}")]
    public void WhenAUiProtocolClientSendsACommandWithRequestId(string type, string requestId) =>
        myScenario.SendCommand(type, requestId);

    [Then("the workspace tools snapshot reports {string} as available")]
    public void ThenTheWorkspaceToolsSnapshotReportsAsAvailable(string fieldName) =>
        Assert.That(Await(myScenario.WaitForWorkspaceToolsFlagAsync(fieldName)), Is.True,
            $"Expected the workspace tools snapshot to report \"{fieldName}\" as available.");

    [Then("the workspace tools snapshot reports {string} as unavailable")]
    public void ThenTheWorkspaceToolsSnapshotReportsAsUnavailable(string fieldName) =>
        Assert.That(Await(myScenario.WaitForWorkspaceToolsFlagAsync(fieldName)), Is.False,
            $"Expected the workspace tools snapshot to report \"{fieldName}\" as unavailable.");

    /// <summary>Polls for a file appearing directly in the project (workspace) root - the observable, black-box
    /// proof that a workspace tool's own command genuinely launched an external process with the expected
    /// arguments and working directory, without this step ever knowing which tool or process produced it.
    /// </summary>
    [Then("file {string} appears in the project root")]
    public void ThenFileAppearsInTheProjectRoot(string relativePath)
    {
        var path = myScenario.PathInRoot(relativePath);
        var deadline = DateTime.UtcNow + FileAppearsTimeout;
        while (!File.Exists(path) && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(FileAppearsPollInterval);
        }
        Assert.That(File.Exists(path), Is.True, $"Expected file '{relativePath}' to appear in the project root.");
    }

    private static T Await<T>(Task<T> task) => task.GetAwaiter().GetResult();
}
