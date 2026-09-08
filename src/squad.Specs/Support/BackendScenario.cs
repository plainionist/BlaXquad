namespace squad.Specs.Support;

/// <summary>
/// Test-owned lifetime and composition root for one backend-process specification. It wires together the Git
/// workspace, the published, provider-free squad-hq CLI, and the headless UI protocol client so step definitions
/// only ever see semantic, backend-agnostic operations - never a file-system path beyond a user-supplied role
/// name, a process handle, a protocol DTO, or any other product object graph. Implements <see cref="IDisposable"/>
/// as an emergency-only cleanup path: normal specifications call <see cref="ShutdownAsync"/> explicitly, and
/// disposal only forcibly terminates the exact process this instance itself launched - never any other process,
/// even one launched by another concurrently running <see cref="BackendScenario"/>.
/// </summary>
public sealed class BackendScenario : IDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ShutdownGracePeriod = TimeSpan.FromSeconds(5);

    private readonly ScenarioWorkspace myWorkspace;
    private System.Diagnostics.Process? myProcess;
    private HeadlessUiClient? myUi;
    private FakeProviderControlServer? myControl;

    public BackendScenario(ScenarioWorkspace workspace)
    {
        myWorkspace = workspace;
    }

    /// <summary>Whether the backend process has completed the real "ui.ready" handshake.</summary>
    public bool IsReady { get; private set; }

    /// <summary>Whether the launched backend process has not (yet) exited.</summary>
    public bool IsRunning => myProcess is { HasExited: false };

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
    /// Enables the private fake-provider control transport for the next <see cref="StartAsync{TProviderFactory}"/>
    /// call: a uniquely named local pipe and a random per-scenario token that only reach the launched process
    /// through environment variables the fake provider itself reads - never a command-line argument or file.
    /// Returns the server so step definitions can wait for session-start and session-disposal observations.
    /// </summary>
    public FakeProviderControlServer EnableFakeProviderControl()
    {
        myControl = FakeProviderControlServer.Create();
        return myControl;
    }

    /// <summary>
    /// Launches the published, provider-free squad-hq with "--ui stdio" and the given test-owned provider fixture,
    /// completes the real "ui.ready" handshake, and returns only once the process has observably become ready. If
    /// <see cref="EnableFakeProviderControl"/> was called first, also passes its pipe name and token through
    /// environment variables and waits for the fake provider to connect - which happens after "ui.ready", once
    /// the production runtime actually starts establishing sessions.
    /// </summary>
    public async Task StartAsync<TProviderFactory>(TimeSpan? timeout = null)
        where TProviderFactory : squad.AgentProvider.Abstractions.IAgentProviderFactory
    {
        var descriptor = $"{typeof(TProviderFactory).Assembly.Location};{typeof(TProviderFactory).FullName}";
        IReadOnlyDictionary<string, string?>? environment = myControl is null
            ? null
            : new Dictionary<string, string?>
            {
                [FakeProviderControlServer.PipeNameEnvironmentVariable] = myControl.PipeName,
                [FakeProviderControlServer.TokenEnvironmentVariable] = myControl.Token,
            };
        myProcess = myWorkspace.StartProcess(
            myWorkspace.BackendSpecSquadHqExecutablePath,
            ["launch", "--provider", descriptor, "--ui", "stdio", myWorkspace.Root],
            environment,
            redirectStandardInput: true);
        myUi = new HeadlessUiClient(myProcess);
        await myUi.CompleteReadyHandshakeAsync(timeout);
        IsReady = true;
        if (myControl is not null)
        {
            await myControl.WaitForConnectionAsync(timeout);
        }
    }

    /// <summary>
    /// Waits until a "state.snapshot" message reports the given role at the given status, proving a lifecycle
    /// transition (for example a session establishing or disposing) beyond mere process readiness or exit.
    /// </summary>
    public Task WaitForRoleStatusAsync(string role, string status, TimeSpan? timeout = null)
    {
        if (myUi is null)
        {
            throw new InvalidOperationException("The backend process has not been started.");
        }

        return myUi.WaitForRoleStatusAsync(role, status, timeout);
    }

    /// <summary>
    /// Waits until the fake provider has reported, across the private control pipe, that the given role's
    /// session started - proving the session lifecycle from inside the provider process itself, independent of
    /// the UI protocol's own state.snapshot projection. Requires <see cref="EnableFakeProviderControl"/> to have
    /// been called before <see cref="StartAsync{TProviderFactory}"/>.
    /// </summary>
    public Task WaitForRoleSessionStartedAsync(string role, TimeSpan? timeout = null) =>
        RequireControl().WaitForSessionStartedAsync(role, timeout);

    /// <summary>
    /// Waits until the fake provider has reported, across the private control pipe, that the given role's
    /// session was disposed. Requires <see cref="EnableFakeProviderControl"/> to have been called before
    /// <see cref="StartAsync{TProviderFactory}"/>.
    /// </summary>
    public Task WaitForRoleSessionDisposedAsync(string role, TimeSpan? timeout = null) =>
        RequireControl().WaitForSessionDisposedAsync(role, timeout);

    private FakeProviderControlServer RequireControl() =>
        myControl ?? throw new InvalidOperationException(
            $"{nameof(EnableFakeProviderControl)} must be called before starting the backend scenario.");

    /// <summary>
    /// Requests shutdown through the real "squad-hq shutdown" host-control command and awaits the launched
    /// process's own clean exit, returning the exit code it observed - never the process itself.
    /// </summary>
    public Task<int> ShutdownAsync(TimeSpan? timeout = null)
    {
        if (myProcess is null || myUi is null)
        {
            throw new InvalidOperationException("The backend process has not been started.");
        }

        myWorkspace.RunBackendSpecSquadHq(["shutdown", myWorkspace.Root]);

        var deadlineMilliseconds = (int)(timeout ?? DefaultTimeout).TotalMilliseconds;
        if (!myProcess.WaitForExit(deadlineMilliseconds))
        {
            throw new HeadlessUiWaitTimeoutException(
                "the backend process to exit after requesting shutdown",
                myUi.DescribeDiagnostics());
        }

        return Task.FromResult(myProcess.ExitCode);
    }

    /// <summary>
    /// Emergency cleanup for a scenario that never reached, or never completed, a normal host-control shutdown.
    /// Requests shutdown one more time on a best-effort basis, waits a bounded grace period for the exact process
    /// this scenario launched to exit on its own, and only then forcibly terminates that same process - never any
    /// other process, even one launched by another concurrently running scenario. Never throws, so a cleanup
    /// failure here can never replace a scenario's real failure.
    /// </summary>
    public void Dispose()
    {
        try
        {
            DisposeControl();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"BackendScenario cleanup: control pipe disposal failed: {exception.Message}");
        }

        if (myProcess is not { HasExited: false } process)
        {
            return;
        }

        try
        {
            myWorkspace.RunBackendSpecSquadHq(["shutdown", myWorkspace.Root]);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"BackendScenario cleanup: best-effort shutdown request failed: {exception.Message}");
        }

        try
        {
            if (!process.WaitForExit((int)ShutdownGracePeriod.TotalMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit((int)DefaultTimeout.TotalMilliseconds);
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"BackendScenario cleanup: forced termination failed: {exception.Message}");
        }
    }

    private void DisposeControl()
    {
        if (myControl is not null)
        {
            myControl.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
