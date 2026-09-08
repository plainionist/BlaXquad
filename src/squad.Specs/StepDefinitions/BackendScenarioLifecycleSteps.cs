using squad.Specs.Support;

namespace squad.Specs.StepDefinitions;

/// <summary>
/// Proves two lifecycle invariants that step definitions cannot exercise through <see cref="BackendScenario"/>'s
/// own semantic operations alone: emergency cleanup terminates only the exact process a scenario itself launched,
/// even while another headquarters process from a different scenario is still running, and a temporary
/// workspace's cleanup is bounded and never lets a cleanup failure escape <see cref="ScenarioWorkspace.Dispose"/>.
/// Owns a handful of extra <see cref="ScenarioWorkspace"/> instances directly (rather than the single one Reqnroll
/// injects into a scenario) because each independent backend process needs its own isolated Git project root.
/// </summary>
[Binding]
public sealed class BackendScenarioLifecycleSteps
{
    private readonly Dictionary<string, ScenarioWorkspace> myWorkspaces = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BackendScenario> myScenarios = new(StringComparer.Ordinal);
    private ScenarioWorkspace? myLockedWorkspace;
    private FileStream? myLockedFile;
    private Exception? myDisposeException;
    private TimeSpan myDisposeDuration;

    [Given("two independent backend scenarios {string} and {string}, each configured with a {string} role")]
    public void GivenTwoIndependentBackendScenariosEachConfiguredWithARole(string firstLabel, string secondLabel, string role)
    {
        CreateScenario(firstLabel, role);
        CreateScenario(secondLabel, role);
    }

    [Given("both backend scenarios have started squad-hq with the echo provider fixture")]
    public void GivenBothBackendScenariosHaveStartedSquadHqWithTheEchoProviderFixture()
    {
        foreach (var scenario in myScenarios.Values)
        {
            Await(scenario.StartAsync<EchoAgentProviderFactory>());
        }
    }

    [When("the {string} backend scenario is disposed without a normal shutdown")]
    public void WhenTheBackendScenarioIsDisposedWithoutANormalShutdown(string label) => myScenarios[label].Dispose();

    [Then("the {string} backend scenario process has exited")]
    public void ThenTheBackendScenarioProcessHasExited(string label) =>
        Assert.That(myScenarios[label].IsRunning, Is.False);

    [Then("the {string} backend scenario process is still running")]
    public void ThenTheBackendScenarioProcessIsStillRunning(string label) =>
        Assert.That(myScenarios[label].IsRunning, Is.True);

    [Given("a git project workspace with a file locked open inside it")]
    public void GivenAGitProjectWorkspaceWithAFileLockedOpenInsideIt()
    {
        myLockedWorkspace = new ScenarioWorkspace();
        myLockedWorkspace.InitializeGitRepository();
        var lockedPath = myLockedWorkspace.PathInWorkspace("locked.txt");
        File.WriteAllText(lockedPath, "locked");
        myLockedFile = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    [When("the workspace is disposed")]
    public void WhenTheWorkspaceIsDisposed()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            myLockedWorkspace!.Dispose();
        }
        catch (Exception exception)
        {
            myDisposeException = exception;
        }
        finally
        {
            stopwatch.Stop();
            myDisposeDuration = stopwatch.Elapsed;
        }
    }

    [Then("disposal completes without throwing within the bounded cleanup window")]
    public void ThenDisposalCompletesWithoutThrowingWithinTheBoundedCleanupWindow() =>
        Assert.Multiple(() =>
        {
            Assert.That(myDisposeException, Is.Null, "Dispose must never let a cleanup failure escape.");
            Assert.That(myDisposeDuration, Is.LessThan(TimeSpan.FromSeconds(30)), "Dispose must be bounded.");
        });

    [AfterScenario]
    public void CleanUp()
    {
        foreach (var scenario in myScenarios.Values)
        {
            scenario.Dispose();
        }
        foreach (var workspace in myWorkspaces.Values)
        {
            workspace.Dispose();
        }

        myLockedFile?.Dispose();

        // The workspace under test may have failed to remove its directory while the file above was still locked
        // open; now that the lock is released, remove any leftovers so the fixture does not linger on disk.
        if (myLockedWorkspace is { Root: var root } && Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private void CreateScenario(string label, string role)
    {
        var workspace = new ScenarioWorkspace();
        myWorkspaces[label] = workspace;
        var scenario = new BackendScenario(workspace);
        scenario.ConfigureRole(role);
        myScenarios[label] = scenario;
    }

    private static void Await(Task task) => task.GetAwaiter().GetResult();
}
