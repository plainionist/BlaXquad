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
    /// Creates one Git project configured with a real linked worktree per given role (delegating the durable
    /// layout to <see cref="ScenarioWorkspace.ConfigureProject"/>), for scenarios that need more than one role's
    /// session live in the same launched process - for example a sender role whose own "squad handoff" CLI
    /// invocation and a recipient role whose fake session observes the resulting wake-up.
    /// </summary>
    public IReadOnlyDictionary<string, string> ConfigureRoles(params string[] roles) => myWorkspace.ConfigureProject(roles);

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
    /// the production runtime actually starts establishing sessions. Unless <paramref name="continueLaunch"/> is
    /// set, a plain launch resets configured worktrees and clears existing handoff queues, matching a genuine
    /// first launch; <paramref name="continueLaunch"/> passes the real "--continue" flag so a scenario can resume
    /// against durable state a prior launch (or test fixture) already left on disk, exactly like a real restart.
    /// </summary>
    public async Task StartAsync<TProviderFactory>(TimeSpan? timeout = null, bool continueLaunch = false)
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
        IReadOnlyList<string> launchArguments = continueLaunch
            ? ["launch", "--continue", "--provider", descriptor, "--ui", "stdio", myWorkspace.Root]
            : ["launch", "--provider", descriptor, "--ui", "stdio", myWorkspace.Root];
        myProcess = myWorkspace.StartProcess(
            myWorkspace.BackendSpecSquadHqExecutablePath,
            launchArguments,
            environment,
            redirectStandardInput: true);
        myUi = new HeadlessUiClient(myProcess);
        await myUi.CompleteReadyHandshakeAsync(timeout, DescribeControlDiagnostics());
        IsReady = true;
        if (myControl is not null)
        {
            await myControl.WaitForConnectionAsync(timeout, DescribeUiDiagnostics());
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

        return myUi.WaitForRoleStatusAsync(role, status, timeout, DescribeControlDiagnostics());
    }

    /// <summary>Sends a prompt to the given role through the real UI protocol - the same path a real user
    /// interface uses, never a shortcut into the provider.</summary>
    public void SendPrompt(string role, string prompt)
    {
        if (myUi is null)
        {
            throw new InvalidOperationException("The backend process has not been started.");
        }

        myUi.SendPrompt(role, prompt);
    }

    /// <summary>Aborts the given role's current operation through the real UI protocol's "role.abort" command.</summary>
    public void RequestAbort(string role) => RequireUi().SendAbort(role);

    /// <summary>Responds to a permission request through the real UI protocol's "permission.respond" command.</summary>
    public void RespondToPermission(string role, string requestId, bool approved) =>
        RequireUi().RespondToPermission(role, requestId, approved);

    /// <summary>Responds to an input request through the real UI protocol's "input.respond" command.</summary>
    public void RespondToInput(string role, string requestId, string? answer, bool wasFreeform = true) =>
        RequireUi().RespondToInput(role, requestId, answer, wasFreeform);

    /// <summary>Responds to an elicitation request through the real UI protocol's "elicitation.respond"
    /// command, optionally carrying accepted content (for example a form value) alongside the chosen
    /// action.</summary>
    public void RespondToElicitation(string role, string requestId, string action, object? content = null) =>
        RequireUi().RespondToElicitation(role, requestId, action, content);

    /// <summary>Waits until a "state.snapshot" message publishes a pending permission request with the given
    /// role, request id, and description - proving every supported field of the published interaction.</summary>
    public Task WaitForPendingPermissionAsync(string role, string requestId, string description, TimeSpan? timeout = null) =>
        RequireUi().WaitForPendingPermissionAsync(role, requestId, description, timeout, DescribeControlDiagnostics());

    /// <summary>Waits until a "state.snapshot" message no longer publishes the given pending permission request
    /// for the given role - proving a terminal role failure genuinely removed it.</summary>
    public Task WaitForNoPendingPermissionAsync(string role, string requestId, TimeSpan? timeout = null) =>
        RequireUi().WaitForNoPendingPermissionAsync(role, requestId, timeout, DescribeControlDiagnostics());

    /// <summary>Waits until a "state.snapshot" message publishes a pending input request with the given role,
    /// request id, prompt, choices, and freeform support - proving every supported field of the published
    /// interaction.</summary>
    public Task WaitForPendingInputAsync(
        string role, string requestId, string prompt, IReadOnlyList<string>? choices, bool allowFreeform, TimeSpan? timeout = null) =>
        RequireUi().WaitForPendingInputAsync(role, requestId, prompt, choices, allowFreeform, timeout, DescribeControlDiagnostics());

    /// <summary>Waits until a "state.snapshot" message publishes a pending elicitation request with the given
    /// role, request id, prompt, mode, and URL - proving every supported field of the published
    /// interaction.</summary>
    public Task WaitForPendingElicitationAsync(
        string role, string requestId, string prompt, string mode, string? url = null, TimeSpan? timeout = null) =>
        RequireUi().WaitForPendingElicitationAsync(role, requestId, prompt, mode, url, timeout, DescribeControlDiagnostics());

    /// <summary>Waits for a "protocol.error" message and returns its human-readable message - the observable
    /// outcome of a wrong-role, duplicate, or late interaction response. <paramref name="skip"/> skips that many
    /// earlier protocol errors already observed, so a caller can prove a later command produced its own new error
    /// instead of re-matching one already caused by an earlier command.</summary>
    public Task<string> WaitForProtocolErrorAsync(int skip = 0, TimeSpan? timeout = null) => RequireUi().WaitForProtocolErrorAsync(skip, timeout);

    /// <summary>Waits until the given role's real transcript, projected from production provider events, contains
    /// the given content - proving a reply crossed all the way from the provider into the observable UI state.
    /// </summary>
    public Task WaitForTranscriptAsync(string role, string content, TimeSpan? timeout = null)
    {
        if (myUi is null)
        {
            throw new InvalidOperationException("The backend process has not been started.");
        }

        return myUi.WaitForTranscriptAsync(role, content, timeout, DescribeControlDiagnostics());
    }

    /// <summary>Waits until a "state.snapshot" message reports the given role at the given AI-credit usage.</summary>
    public Task WaitForRoleUsageAsync(string role, decimal aicUsed, TimeSpan? timeout = null) =>
        RequireUi().WaitForRoleUsageAsync(role, aicUsed, timeout, DescribeControlDiagnostics());

    private HeadlessUiClient RequireUi() =>
        myUi ?? throw new InvalidOperationException("The backend process has not been started.");

    /// <summary>
    /// Returns the narrow, semantic role controller for the given role: the only surface step definitions use to
    /// observe prompts and send replies, keeping every control-pipe DTO and provider <c>AgentEvent</c> value
    /// behind this API. Requires <see cref="EnableFakeProviderControl"/> to have been called before
    /// <see cref="StartAsync{TProviderFactory}"/>.
    /// </summary>
    public BackendScenarioAgent Agent(string role) => new(RequireControl(), role, DescribeUiDiagnostics());

    /// <summary>
    /// Waits until the fake provider has reported, across the private control pipe, that the given role's
    /// session started - proving the session lifecycle from inside the provider process itself, independent of
    /// the UI protocol's own state.snapshot projection. Requires <see cref="EnableFakeProviderControl"/> to have
    /// been called before <see cref="StartAsync{TProviderFactory}"/>.
    /// </summary>
    public Task WaitForRoleSessionStartedAsync(string role, TimeSpan? timeout = null) =>
        RequireControl().WaitForSessionStartedAsync(role, timeout, DescribeUiDiagnostics());

    /// <summary>
    /// Waits until the fake provider has reported, across the private control pipe, that the given role's
    /// session was disposed. Requires <see cref="EnableFakeProviderControl"/> to have been called before
    /// <see cref="StartAsync{TProviderFactory}"/>.
    /// </summary>
    public Task WaitForRoleSessionDisposedAsync(string role, TimeSpan? timeout = null) =>
        RequireControl().WaitForSessionDisposedAsync(role, timeout, DescribeUiDiagnostics());

    private FakeProviderControlServer RequireControl() =>
        myControl ?? throw new InvalidOperationException(
            $"{nameof(EnableFakeProviderControl)} must be called before starting the backend scenario.");

    /// <summary>Describes this scenario's control-pipe observations, for callers waiting on the UI protocol side
    /// so a timeout reports process, UI, and provider state in one block - or null if no control transport was
    /// enabled, so a UI-only wait's diagnostics are not misleadingly padded with an empty provider section.</summary>
    private Func<string>? DescribeControlDiagnostics() => myControl is null ? null : myControl.DescribeDiagnostics;

    /// <summary>Describes this scenario's process and UI protocol state, for callers waiting on the control-pipe
    /// side so a timeout reports process, UI, and provider state in one block - or null before the backend process
    /// has started.</summary>
    private Func<string>? DescribeUiDiagnostics() => myUi is null ? null : myUi.DescribeDiagnostics;

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
                myUi.DescribeDiagnostics(DescribeControlDiagnostics()));
        }

        return Task.FromResult(myProcess.ExitCode);
    }

    /// <summary>
    /// Starts "squad-hq wait-for-agent" for the given role as a separate real child process addressing this
    /// scenario's project, using the same published, provider-free executable as the launched host, and returns a
    /// semantic handle so a specification can observe whether the command remains blocked or await its bounded
    /// completion and captured output - never the raw process itself.
    /// </summary>
    public BackendScenarioCommand StartWaitForAgent(string role, TimeSpan timeout)
    {
        var process = myWorkspace.StartProcess(
            myWorkspace.BackendSpecSquadHqExecutablePath,
            [
                "wait-for-agent",
                role,
                "--timeout",
                timeout.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                myWorkspace.Root,
            ]);
        return new BackendScenarioCommand(process);
    }

    /// <summary>
    /// Abruptly terminates the exact squad-hq process this scenario launched - simulating a real host crash
    /// instead of a normal "squad-hq shutdown" - and waits until it has actually exited, so a specification can
    /// prove stale-ownership recovery starts from a genuinely terminated process rather than fabricated metadata.
    /// </summary>
    public void Terminate(TimeSpan? timeout = null)
    {
        if (myProcess is not { HasExited: false } process)
        {
            throw new InvalidOperationException("The backend process has not been started, or has already exited.");
        }

        process.Kill(entireProcessTree: true);
        var deadlineMilliseconds = (int)(timeout ?? DefaultTimeout).TotalMilliseconds;
        if (!process.WaitForExit(deadlineMilliseconds))
        {
            throw new HeadlessUiWaitTimeoutException(
                "the backend process to exit after deliberate termination",
                myUi?.DescribeDiagnostics(DescribeControlDiagnostics()) ?? ProcessDiagnostics.Describe(process));
        }
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
        if (myControl is null)
        {
            return;
        }

        var undisposed = myControl.DescribeUndisposedSessions();
        if (undisposed is not null)
        {
            Console.Error.WriteLine($"BackendScenario cleanup: sessions started but never disposed:\n{undisposed}");
        }
        myControl.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
