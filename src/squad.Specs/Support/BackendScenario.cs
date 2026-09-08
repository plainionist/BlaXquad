namespace squad.Specs.Support;

/// <summary>
/// Test-owned lifetime and composition root for one backend-process specification. It wires together the Git
/// workspace, the published, provider-free squad-hq CLI, and the headless UI protocol client so step definitions
/// only ever see semantic, backend-agnostic operations - never a file-system path beyond a user-supplied role
/// name, a process handle, a protocol DTO, or any other product object graph.
/// </summary>
public sealed class BackendScenario
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private readonly ScenarioWorkspace myWorkspace;
    private System.Diagnostics.Process? myProcess;

    public BackendScenario(ScenarioWorkspace workspace)
    {
        myWorkspace = workspace;
    }

    /// <summary>Whether the backend process has completed the real "ui.ready" handshake.</summary>
    public bool IsReady { get; private set; }

    /// <summary>Creates one Git project configured with a single role at the project root worktree.</summary>
    public void ConfigureRole(string role)
    {
        myWorkspace.InitializeGitRepository();
        myWorkspace.WriteFile("blaxquad/constitution.prompt", "Follow the project constitution.\n");
        myWorkspace.WriteFile(
            "blaxquad/squad.json",
            $$"""
            {
              "roles": [
                { "name": "{{role}}", "worktree": "master", "agent": {} }
              ]
            }
            """ + "\n");
        myWorkspace.WriteFile($"blaxquad/roles/{role}.prompt", $"Act as the {role}.\n");
    }

    /// <summary>
    /// Launches the published, provider-free squad-hq with "--ui stdio" and the given test-owned provider fixture,
    /// completes the real "ui.ready" handshake, and returns only once the process has observably become ready.
    /// </summary>
    public async Task StartAsync<TProviderFactory>(TimeSpan? timeout = null)
        where TProviderFactory : squad.AgentProvider.Abstractions.IAgentProviderFactory
    {
        var descriptor = $"{typeof(TProviderFactory).Assembly.Location};{typeof(TProviderFactory).FullName}";
        myProcess = myWorkspace.StartProcess(
            myWorkspace.BackendSpecSquadHqExecutablePath,
            ["launch", "--provider", descriptor, "--ui", "stdio", myWorkspace.Root],
            redirectStandardInput: true);
        var ui = new HeadlessUiClient(myProcess);
        await ui.CompleteReadyHandshakeAsync(timeout);
        IsReady = true;
    }

    /// <summary>
    /// Requests shutdown through the real "squad-hq shutdown" host-control command and awaits the launched
    /// process's own clean exit, returning the exit code it observed - never the process itself.
    /// </summary>
    public Task<int> ShutdownAsync(TimeSpan? timeout = null)
    {
        if (myProcess is null)
        {
            throw new InvalidOperationException("The backend process has not been started.");
        }

        myWorkspace.RunBackendSpecSquadHq(["shutdown", myWorkspace.Root]);

        var deadlineMilliseconds = (int)(timeout ?? DefaultTimeout).TotalMilliseconds;
        if (!myProcess.WaitForExit(deadlineMilliseconds))
        {
            throw new TimeoutException("Timed out waiting for the backend process to exit after requesting shutdown.");
        }

        return Task.FromResult(myProcess.ExitCode);
    }
}
