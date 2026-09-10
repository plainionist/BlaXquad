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
    private string? myIsolatedTempDirectory;
    private int? myStartupGateAfterSessions;
    private bool myFailProviderBeforeRuntime;
    private int? myFailProviderAfterSessions;
    private string? myFailProviderDisposalMessage;

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
    /// Redirects the next <see cref="StartAsync{TProviderFactory}"/> launch's temporary-directory environment
    /// variables (<c>TEMP</c>/<c>TMP</c> on Windows, <c>TMPDIR</c> elsewhere) to a fresh directory exclusively
    /// owned by this scenario. Production keeps its on-disk transcript history archive's generated name and full
    /// path private - never exposed through the UI protocol - so proving it is created, and later removed on clean
    /// shutdown, without ever risking a collision with another concurrently running scenario's own temporary files
    /// requires sandboxing the launched process's entire temporary-directory root to one this scenario alone
    /// writes into.
    /// </summary>
    public string IsolateTemporaryDirectory()
    {
        myIsolatedTempDirectory = myWorkspace.PathInWorkspace("isolated-temp");
        Directory.CreateDirectory(myIsolatedTempDirectory);
        return myIsolatedTempDirectory;
    }

    /// <summary>Whether this scenario's isolated temporary directory (see <see cref="IsolateTemporaryDirectory"/>)
    /// currently contains a transcript history archive - proving the launched process created one - without ever
    /// exposing its generated name.</summary>
    public bool HasTemporaryTranscriptHistory()
    {
        if (myIsolatedTempDirectory is null)
        {
            throw new InvalidOperationException(
                "The scenario's temporary directory was never isolated; call IsolateTemporaryDirectory() before starting the process.");
        }
        return Directory.Exists(myIsolatedTempDirectory)
            && Directory.GetDirectories(myIsolatedTempDirectory, "blaxquad-transcript-history-*").Length > 0;
    }

    /// <summary>
    /// Launches the published, provider-free squad-hq with "--ui stdio" and the given test-owned provider fixture,
    /// completes the real "ui.ready" handshake, and returns only once the process has observably become ready. If
    /// <see cref="EnableFakeProviderControl"/> was called first, also passes its pipe name and token through
    /// environment variables and waits for the fake provider to connect - which happens after "ui.ready", once
    /// the production runtime actually starts establishing sessions. If <see cref="IsolateTemporaryDirectory"/>
    /// was called first, also redirects the launched process's own temporary-directory environment variables to
    /// that isolated directory. Unless <paramref name="continueLaunch"/> is set, a plain launch resets configured
    /// worktrees and clears existing handoff queues, matching a genuine first launch; <paramref name="continueLaunch"/>
    /// passes the real "--continue" flag so a scenario can resume against durable state a prior launch (or test
    /// fixture) already left on disk, exactly like a real restart.
    /// </summary>
    public async Task StartAsync<TProviderFactory>(TimeSpan? timeout = null, bool continueLaunch = false)
        where TProviderFactory : squad.AgentProvider.Abstractions.IAgentProviderFactory
    {
        var descriptor = $"{typeof(TProviderFactory).Assembly.Location};{typeof(TProviderFactory).FullName}";
        var environmentOverrides = new Dictionary<string, string?>();
        if (myControl is not null)
        {
            environmentOverrides[FakeProviderControlServer.PipeNameEnvironmentVariable] = myControl.PipeName;
            environmentOverrides[FakeProviderControlServer.TokenEnvironmentVariable] = myControl.Token;
        }
        if (myIsolatedTempDirectory is not null)
        {
            environmentOverrides["TEMP"] = myIsolatedTempDirectory;
            environmentOverrides["TMP"] = myIsolatedTempDirectory;
            environmentOverrides["TMPDIR"] = myIsolatedTempDirectory;
        }
        if (myStartupGateAfterSessions is { } gateAfterSessions)
        {
            environmentOverrides[FakeProviderControlServer.StartupGateAfterSessionsEnvironmentVariable] =
                gateAfterSessions.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        if (myFailProviderBeforeRuntime)
        {
            environmentOverrides[FakeProviderControlServer.FailBeforeRuntimeEnvironmentVariable] = "true";
        }
        if (myFailProviderAfterSessions is { } failAfterSessions)
        {
            environmentOverrides[FakeProviderControlServer.FailAfterSessionsEnvironmentVariable] =
                failAfterSessions.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        if (myFailProviderDisposalMessage is { } failDisposalMessage)
        {
            environmentOverrides[FakeProviderControlServer.FailDisposalMessageEnvironmentVariable] = failDisposalMessage;
        }
        IReadOnlyDictionary<string, string?>? environment = environmentOverrides.Count == 0 ? null : environmentOverrides;
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
    /// Launches the published, provider-free squad-hq exactly like <see cref="StartAsync{TProviderFactory}"/>, but
    /// through <see cref="CancellableChildProcess"/> instead of an ordinary process launch, so the process owns
    /// its own process group and <see cref="RequestCallerCancellation"/> can later deliver the platform's normal
    /// cancellation signal to it alone - never the test runner, and never any other concurrently running
    /// scenario's own child process. Deliberately narrower than <see cref="StartAsync{TProviderFactory}"/> - no
    /// "--continue" or temporary-directory isolation support - since only the caller-cancellation specification
    /// scenario needs this launcher.
    /// </summary>
    public async Task StartCancellableAsync<TProviderFactory>(TimeSpan? timeout = null)
        where TProviderFactory : squad.AgentProvider.Abstractions.IAgentProviderFactory
    {
        if (!CancellableChildProcess.CanDeliverIsolatedSignal)
        {
            throw new PlatformNotSupportedException(
                "This platform cannot isolate cancellation-signal delivery to one child process; StartCancellableAsync is unsupported here.");
        }

        var descriptor = $"{typeof(TProviderFactory).Assembly.Location};{typeof(TProviderFactory).FullName}";
        var environmentOverrides = new Dictionary<string, string?>();
        if (myControl is not null)
        {
            environmentOverrides[FakeProviderControlServer.PipeNameEnvironmentVariable] = myControl.PipeName;
            environmentOverrides[FakeProviderControlServer.TokenEnvironmentVariable] = myControl.Token;
        }

        myProcess = CancellableChildProcess.Start(
            myWorkspace.BackendSpecSquadHqExecutablePath,
            ["launch", "--provider", descriptor, "--ui", "stdio", myWorkspace.Root],
            myWorkspace.Root,
            environmentOverrides.Count == 0 ? null : environmentOverrides,
            out var standardInput,
            out var standardOutput,
            out var standardError);
        myWorkspace.TrackProcess(myProcess);
        myUi = new HeadlessUiClient(myProcess, standardInput, standardOutput, standardError);
        await myUi.CompleteReadyHandshakeAsync(timeout, DescribeControlDiagnostics());
        IsReady = true;
        if (myControl is not null)
        {
            await myControl.WaitForConnectionAsync(timeout, DescribeUiDiagnostics());
        }
    }

    /// <summary>
    /// Launches the published, provider-free squad-hq exactly like <see cref="StartAsync{TProviderFactory}"/>, but
    /// returns immediately once the process starts without ever completing the "ui.ready" handshake - for the
    /// specification proving standard input closed (or a host-control shutdown requested) before readiness still
    /// terminates the process cleanly, rather than the ordinary ready-then-close sequence, and for a provider
    /// startup failure configured through <see cref="FailProviderBeforeRuntime"/> or
    /// <see cref="FailProviderAfterSessions"/> - either of which prevents readiness from ever being reached.
    /// Wires the same fake-provider control-pipe, startup-gate, and failure-mode environment as
    /// <see cref="StartAsync{TProviderFactory}"/> when <see cref="EnableFakeProviderControl"/>,
    /// <see cref="GateProviderStartupAfterSessions"/>, <see cref="FailProviderBeforeRuntime"/>, or
    /// <see cref="FailProviderAfterSessions"/> were called, so a fake-provider launch through this seam can still
    /// be observed across the control pipe.
    /// </summary>
    public void LaunchWithoutReadyHandshake<TProviderFactory>()
        where TProviderFactory : squad.AgentProvider.Abstractions.IAgentProviderFactory
    {
        var descriptor = $"{typeof(TProviderFactory).Assembly.Location};{typeof(TProviderFactory).FullName}";
        var environmentOverrides = new Dictionary<string, string?>();
        if (myControl is not null)
        {
            environmentOverrides[FakeProviderControlServer.PipeNameEnvironmentVariable] = myControl.PipeName;
            environmentOverrides[FakeProviderControlServer.TokenEnvironmentVariable] = myControl.Token;
        }
        if (myStartupGateAfterSessions is { } gateAfterSessions)
        {
            environmentOverrides[FakeProviderControlServer.StartupGateAfterSessionsEnvironmentVariable] =
                gateAfterSessions.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        if (myFailProviderBeforeRuntime)
        {
            environmentOverrides[FakeProviderControlServer.FailBeforeRuntimeEnvironmentVariable] = "true";
        }
        if (myFailProviderAfterSessions is { } failAfterSessions)
        {
            environmentOverrides[FakeProviderControlServer.FailAfterSessionsEnvironmentVariable] =
                failAfterSessions.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        if (myFailProviderDisposalMessage is { } failDisposalMessage)
        {
            environmentOverrides[FakeProviderControlServer.FailDisposalMessageEnvironmentVariable] = failDisposalMessage;
        }
        IReadOnlyDictionary<string, string?>? environment = environmentOverrides.Count == 0 ? null : environmentOverrides;
        myProcess = myWorkspace.StartProcess(
            myWorkspace.BackendSpecSquadHqExecutablePath,
            ["launch", "--provider", descriptor, "--ui", "stdio", myWorkspace.Root],
            environment,
            redirectStandardInput: true);
        myUi = new HeadlessUiClient(myProcess);
    }

    /// <summary>
    /// Closes the launched process's standard input - the same observable event as its UI process exiting or its
    /// window closing - proving squad-hq treats end of standard input as "the UI is gone" and terminates cleanly,
    /// without ever depending on a recording window test double.
    /// </summary>
    public void CloseStandardInput() => RequireUi().CloseStandardInput();

    /// <summary>
    /// Delivers the platform's own normal cancellation signal (the same one a real terminal's Ctrl+C would send)
    /// to the exact process this scenario launched through <see cref="StartCancellableAsync{TProviderFactory}"/> -
    /// never broadcasting to the test runner or to any other concurrently running scenario's own child process.
    /// </summary>
    public void RequestCallerCancellation()
    {
        if (myProcess is null)
        {
            throw new InvalidOperationException("The backend process has not been started.");
        }
        CancellableChildProcess.SendCancellationSignal(myProcess);
    }

    /// <summary>
    /// Waits for the launched process to exit on its own - without first requesting shutdown through any
    /// host-control command - and returns the exit code it observed, for specifications proving that closing
    /// standard input or delivering the platform's cancellation signal alone terminates the process cleanly.
    /// </summary>
    public Task<int> WaitForProcessExitAsync(TimeSpan? timeout = null)
    {
        if (myProcess is null || myUi is null)
        {
            throw new InvalidOperationException("The backend process has not been started.");
        }

        var deadlineMilliseconds = (int)(timeout ?? DefaultTimeout).TotalMilliseconds;
        if (!myProcess.WaitForExit(deadlineMilliseconds))
        {
            throw new HeadlessUiWaitTimeoutException(
                "the backend process to exit",
                myUi.DescribeDiagnostics(DescribeControlDiagnostics()));
        }

        // Reads through CancellableChildProcess.GetExitCode rather than myProcess.ExitCode directly: a process
        // obtained through CancellableChildProcess's manual CreateProcess launcher
        // (System.Diagnostics.Process.GetProcessById) never has .NET's own process handle cached merely from
        // observing WaitForExit, and ExitCode throws in that case even though the process has genuinely exited.
        return Task.FromResult(CancellableChildProcess.GetExitCode(myProcess));
    }

    /// <summary>
    /// Waits until the launched process's captured standard error contains the given text, for specifications
    /// proving a startup failure produced a clear CLI diagnostic there - never a raw ".NET Unhandled exception"
    /// dump. Polls rather than snapshotting once, since the process's own exit and its background stderr reader
    /// observing end-of-stream are two independent, unordered events.
    /// </summary>
    public async Task<string> WaitForStandardErrorContainingAsync(string text, TimeSpan? timeout = null)
    {
        if (myUi is null)
        {
            throw new InvalidOperationException("The backend process has not been started.");
        }

        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (true)
        {
            var captured = myUi.CapturedStandardError();
            if (captured.Contains(text, StringComparison.Ordinal))
            {
                return captured;
            }
            if (DateTime.UtcNow >= deadline)
            {
                throw new HeadlessUiWaitTimeoutException(
                    $"standard error to contain '{text}'", myUi.DescribeDiagnostics(DescribeControlDiagnostics()));
            }
            await Task.Delay(TimeSpan.FromMilliseconds(25));
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

    /// <summary>
    /// Waits until the most recently published "state.snapshot" message (not just any snapshot ever observed)
    /// reports the given role at the given status - proving the status still holds after later, possibly stale,
    /// publication rather than merely rematching the same earlier snapshot already observed at termination.
    /// </summary>
    public Task WaitForLatestRoleStatusAsync(string role, string status, TimeSpan? timeout = null) =>
        RequireUi().WaitForLatestRoleStatusAsync(role, status, timeout, DescribeControlDiagnostics());

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

    /// <summary>Sends one already-serialized protocol line verbatim through the launched process's standard
    /// input - see <see cref="HeadlessUiClient.SendRawEnvelope"/> - for specifications proving the real
    /// "protocol.error" validation contract at the wire boundary for envelope shapes (unsupported version,
    /// missing or unknown type, missing role or request id, mistyped payload fields, or unparsable JSON) no
    /// semantic command method could construct.</summary>
    public void SendRawEnvelope(string rawJsonLine) => RequireUi().SendRawEnvelope(rawJsonLine);

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

    /// <summary>Waits until the <paramref name="skip"/>-plus-first "transcript.update" message reports an appended
    /// or replaced entry for the given role with the given source - and, unless null, the given content - and
    /// returns the dashboard protocol's typed sequence, operation, entry index, source, and content fields.</summary>
    public Task<TranscriptUpdateObservation> WaitForTranscriptUpdateAsync(
        string role, string source, string? content = null, TimeSpan? timeout = null, int skip = 0) =>
        RequireUi().WaitForTranscriptUpdateAsync(role, source, content, timeout, DescribeControlDiagnostics(), skip);

    /// <summary>Waits until the <paramref name="skip"/>-plus-first "transcript.update" message reports the given
    /// operation for the given role - and, unless null, the given content - and returns the dashboard protocol's
    /// typed fields. Unlike <see cref="WaitForTranscriptUpdateAsync(string,string,string?,TimeSpan?)"/>, this also
    /// observes an "append-content" continuation, whose delta fragment carries no source of its own.</summary>
    public Task<TranscriptUpdateObservation> WaitForTranscriptUpdateByOperationAsync(
        string role, string operation, string? content = null, int skip = 0, TimeSpan? timeout = null) =>
        RequireUi().WaitForTranscriptUpdateByOperationAsync(role, operation, content, skip, timeout, DescribeControlDiagnostics());

    /// <summary>Requests a fresh full transcript synchronization through the real UI protocol - the same
    /// operation a reconnecting dashboard relies on to rebuild its view.</summary>
    public void RequestTranscriptSynchronization() => RequireUi().RequestTranscriptSynchronization();

    /// <summary>Waits until a "transcript.synchronize" message reports entries for the given role satisfying the
    /// given predicate, and returns the dashboard protocol's typed sequence and indexed, sourced entries for that
    /// role.</summary>
    public Task<TranscriptSynchronizationObservation> WaitForTranscriptSynchronizationAsync(
        string role, Func<IReadOnlyList<TranscriptEntryObservation>, bool> matches, TimeSpan? timeout = null) =>
        RequireUi().WaitForTranscriptSynchronizationAsync(role, matches, timeout, DescribeControlDiagnostics());

    /// <summary>Counts how many "transcript.synchronize" messages including an entry list for the given role have
    /// been captured so far - the skip count to pass to <see cref="WaitForNextTranscriptSynchronizationAsync"/> to
    /// observe only a synchronization published after this point.</summary>
    public int CountTranscriptSynchronizations(string role) => RequireUi().CountTranscriptSynchronizations(role);

    /// <summary>Waits until the <paramref name="skip"/>-plus-first "transcript.synchronize" message for the given
    /// role has been published - identified purely by structural presence, never by content - so a caller can
    /// assert on that specific response's entries directly and genuinely fail if it carries an unexpected
    /// value.</summary>
    public Task<TranscriptSynchronizationObservation> WaitForNextTranscriptSynchronizationAsync(
        string role, int skip, TimeSpan? timeout = null) =>
        RequireUi().WaitForNextTranscriptSynchronizationAsync(role, skip, timeout, DescribeControlDiagnostics());

    /// <summary>Requests the given role's previous transcript page - the entries immediately preceding
    /// <paramref name="beforeIndex"/> - through the real UI protocol, the same operation a dashboard paging back
    /// through older history relies on.</summary>
    public void RequestTranscriptPage(string role, int beforeIndex) => RequireUi().RequestTranscriptPage(role, beforeIndex);

    /// <summary>Waits until the <paramref name="skip"/>-plus-first "transcript.page" message for the given role has
    /// been published, and returns the dashboard protocol's typed ordered entries and "hasMore" field.</summary>
    public Task<TranscriptPageObservation> WaitForTranscriptPageAsync(string role, int skip = 0, TimeSpan? timeout = null) =>
        RequireUi().WaitForTranscriptPageAsync(role, skip, timeout, DescribeControlDiagnostics());

    /// <summary>Requests one already-known entry's authoritative availability for the given role through the real
    /// UI protocol - the same operation a dashboard relies on to resolve an entry evicted from live retention or
    /// to confirm one has rotated out of the archive entirely.</summary>
    public void RequestArchivedEntry(string role, int entryIndex) => RequireUi().RequestArchivedEntry(role, entryIndex);

    /// <summary>Waits until the <paramref name="skip"/>-plus-first "transcript.entry" message for the given role
    /// and entry index has been published, and returns the dashboard protocol's typed sequence, content (null when
    /// unavailable), and archive availability fields.</summary>
    public Task<ArchivedTranscriptEntryObservation> WaitForArchivedEntryAsync(
        string role, int entryIndex, int skip = 0, TimeSpan? timeout = null) =>
        RequireUi().WaitForArchivedEntryAsync(role, entryIndex, skip, timeout, DescribeControlDiagnostics());

    /// <summary>Writes one Markdown issue file directly under the fixed workspace <c>docs/issues</c> directory -
    /// never a subdirectory - so a scenario can arrange the real filesystem catalog the "issues.list" command
    /// discovers.</summary>
    public void WriteIssueFile(string fileName, string content) => myWorkspace.WriteFile($"docs/issues/{fileName}", content);

    /// <summary>Removes the fixed workspace <c>docs/issues</c> directory entirely, if present, so a scenario can
    /// prove a missing issue directory is a successful empty catalog rather than a protocol error.</summary>
    public void RemoveIssuesDirectory()
    {
        var path = myWorkspace.PathInWorkspace("docs", "issues");
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    /// <summary>Creates the fixed workspace <c>docs/issues</c> directory with no files inside it, so a scenario can
    /// prove a present but empty issue directory is a successful empty catalog.</summary>
    public void CreateEmptyIssuesDirectory() => Directory.CreateDirectory(myWorkspace.PathInWorkspace("docs", "issues"));

    /// <summary>Replaces the fixed workspace <c>docs/issues</c> location with a plain file instead of a directory,
    /// so the real "issues.list" command observes a genuine filesystem conflict and reports a correlated protocol
    /// error rather than a misleading empty catalog.</summary>
    public void ReplaceIssuesDirectoryWithFile()
    {
        var path = myWorkspace.PathInWorkspace("docs", "issues");
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "not a directory");
    }

    /// <summary>Requests the fixed workspace issue catalog through the real UI protocol, tagging the request with
    /// the given request ID so the matching "issues.list" response or correlated "protocol.error" can be resolved
    /// without treating an unrelated protocol failure as a catalog failure.</summary>
    public void RequestIssues(string requestId) => RequireUi().RequestIssues(requestId);

    /// <summary>Waits for the "issues.list" response carrying the given request ID, and returns the dashboard
    /// protocol's typed, ordered issue descriptors.</summary>
    public Task<IReadOnlyList<IssueDescriptorObservation>> WaitForIssuesAsync(string requestId, TimeSpan? timeout = null) =>
        RequireUi().WaitForIssuesAsync(requestId, timeout, DescribeControlDiagnostics());

    /// <summary>Waits for a "protocol.error" message carrying the given request ID and returns its human-readable
    /// message - the observable outcome of a genuine issue-catalog filesystem failure.</summary>
    public Task<string> WaitForCorrelatedProtocolErrorAsync(string requestId, TimeSpan? timeout = null) =>
        RequireUi().WaitForCorrelatedProtocolErrorAsync(requestId, timeout);

    /// <summary>Reconciles the most recently published transcript synchronization for the given role with every
    /// transcript update published afterward, exactly as a reconnecting dashboard client must - proving the
    /// reconstructed transcript contains every entry exactly once and in order even when a synchronization
    /// overlaps ongoing or concurrent publication. Does not itself request a synchronization; callers that need
    /// one to race publication request it explicitly through <see cref="RequestTranscriptSynchronization"/>.
    /// Retries until the reconciled entries satisfy the given predicate.</summary>
    public Task<IReadOnlyList<TranscriptEntryObservation>> WaitForReconciledTranscriptAsync(
        string role, Func<IReadOnlyList<TranscriptEntryObservation>, bool> matches, TimeSpan? timeout = null) =>
        RequireUi().WaitForReconciledTranscriptAsync(role, matches, timeout, DescribeControlDiagnostics());


    /// <summary>Waits until a "state.snapshot" message reports the given role at the given AI-credit usage.</summary>
    public Task WaitForRoleUsageAsync(string role, decimal aicUsed, TimeSpan? timeout = null) =>
        RequireUi().WaitForRoleUsageAsync(role, aicUsed, timeout, DescribeControlDiagnostics());

    /// <summary>Waits until a "state.snapshot" message reports the given role at the given working state with the
    /// given context-token and AI-credit usage together - the combined shape an active-usage-refresh policy
    /// scenario needs to prove usage reported while still working reaches the ui before idle, and that idle
    /// preserves the latest reported values.</summary>
    public Task WaitForRoleUsageSnapshotAsync(
        string role, bool isWorking, long contextUsedTokens, long contextLimitTokens, decimal aicUsed, TimeSpan? timeout = null) =>
        RequireUi().WaitForRoleUsageSnapshotAsync(
            role, isWorking, contextUsedTokens, contextLimitTokens, aicUsed, timeout, DescribeControlDiagnostics());

    /// <summary>Waits until a "state.snapshot" message reports the given role at the given active tool - proving
    /// the UI protocol exposes a running tool call as the role's active tool.</summary>
    public Task WaitForRoleActiveToolAsync(string role, string activeTool, TimeSpan? timeout = null) =>
        RequireUi().WaitForRoleActiveToolAsync(role, activeTool, timeout, DescribeControlDiagnostics());

    /// <summary>Waits until the most recently published "state.snapshot" message reports the given role with no
    /// active tool - proving a tool completion genuinely cleared it.</summary>
    public Task WaitForNoActiveToolAsync(string role, TimeSpan? timeout = null) =>
        RequireUi().WaitForNoActiveToolAsync(role, timeout, DescribeControlDiagnostics());

    /// <summary>
    /// A snapshot of everything captured on the launched process's standard error so far. Callers proving an
    /// expected diagnostic reached standard error should use <see cref="WaitForStandardErrorContainingAsync"/>
    /// instead, which polls out the race between process exit and its background stderr reader observing
    /// end-of-stream; this snapshot exists for asserting the absence of unwanted text once that race has already
    /// settled.
    /// </summary>
    public string CapturedStandardError() => RequireUi().CapturedStandardError();

    /// <summary>
    /// A snapshot of every standard-output line captured from the launched process so far, joined with newlines -
    /// for example proving no protocol message has reached standard output yet before "ui.ready" is sent.
    /// </summary>
    public string CapturedStandardOutput() => RequireUi().CapturedStandardOutput();

    /// <summary>
    /// True only if every line captured on standard output so far is one complete, well-formed protocol envelope -
    /// proving concurrent multi-role activity never tears or interleaves a line's framing.
    /// </summary>
    public bool EveryCapturedStandardOutputLineIsAWellFormedEnvelope() =>
        RequireUi().EveryCapturedStandardOutputLineIsAWellFormedEnvelope();

    /// <summary>
    /// True only if standard error carries no line that looks like a protocol envelope - proving the protocol and
    /// process-diagnostic output streams stay genuinely separate.
    /// </summary>
    public bool StandardErrorContainsNoProtocolEnvelope() => RequireUi().StandardErrorContainsNoProtocolEnvelope();

    /// <summary>
    /// Sends "ui.ready" and awaits the real handshake response after a launch that deliberately deferred it
    /// (<see cref="LaunchWithoutReadyHandshake{TProviderFactory}"/>) - proving the ready gate itself, that no
    /// protocol message is published until this handshake completes. Also awaits the fake-provider control pipe's
    /// connection when <see cref="EnableFakeProviderControl"/> was called, mirroring <see
    /// cref="StartAsync{TProviderFactory}"/>'s own post-handshake connection wait, so callers can safely observe
    /// role sessions across the control pipe immediately afterward.
    /// </summary>
    public async Task CompleteReadyHandshakeAsync(TimeSpan? timeout = null)
    {
        await RequireUi().CompleteReadyHandshakeAsync(timeout, DescribeControlDiagnostics());
        IsReady = true;
        if (myControl is not null)
        {
            await myControl.WaitForConnectionAsync(timeout, DescribeUiDiagnostics());
        }
    }

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

    /// <summary>
    /// Faults the fake provider's whole backend with the given message across the private control pipe, as
    /// production <see cref="squad.AgentProvider.Abstractions.IAgentBackendFailureSource.Failure"/> faulting - a
    /// fatal, backend-wide failure independent of any individual role's session. Requires
    /// <see cref="EnableFakeProviderControl"/> to have been called before <see cref="StartAsync{TProviderFactory}"/>.
    /// </summary>
    public Task FailProviderBackendAsync(string message, TimeSpan? timeout = null) =>
        RequireControl().FailBackendAsync(message, timeout, DescribeUiDiagnostics());

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
    /// Issues the real "squad-hq shutdown" host-control command as its own background process and returns as soon
    /// as it has been started, without waiting for the host to release ownership or for either the shutdown
    /// command or the launched process itself to exit, so a scenario can observe backend cleanup unfold - for
    /// example holding at the real provider boundary through
    /// <see cref="BackendScenarioAgent.ArmPendingDisposalAsync"/> - before later waiting for the process's own
    /// bounded exit through <see cref="WaitForProcessExitAsync"/>. Awaiting the full "squad-hq shutdown" CLI
    /// command's own completion (as <see cref="ShutdownAsync"/> does) cannot be used here because it always blocks
    /// up to its own timeout waiting for host release, which would deadlock against a still-held disposal.
    /// </summary>
    public Task RequestShutdownWithoutWaitingForExit()
    {
        if (myProcess is null)
        {
            throw new InvalidOperationException("The backend process has not been started.");
        }

        myWorkspace.StartProcess(myWorkspace.BackendSpecSquadHqExecutablePath, ["shutdown", myWorkspace.Root]);
        return Task.CompletedTask;
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
    /// Runs the real "squad-hq wait-for-agent" host-control command for the given role against this scenario's own
    /// workspace and awaits its bounded completion, proving agent readiness through the same public command a real
    /// caller uses - never the UI protocol's own "state.snapshot" projection.
    /// </summary>
    public async Task<CommandResult> WaitForAgentReadyThroughCliAsync(string role, TimeSpan? timeout = null)
    {
        var waitTimeout = timeout ?? DefaultTimeout;
        var command = StartWaitForAgent(role, waitTimeout);
        // The outer wait must outlast the command's own "--timeout" so a genuinely ready agent always completes
        // first; the extra margin only bounds how long a broken wait-for-agent is allowed to hang.
        return await command.WaitForCompletionAsync(waitTimeout + TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Confirms this scenario's host-control endpoint is no longer reachable - for example after a normal
    /// host-controlled shutdown - by invoking the real "squad-hq wait-for-agent" command against this scenario's
    /// own workspace and returning its captured result. A live host would answer promptly; an unavailable one
    /// makes this command fail fast with the same "squad host unavailable" diagnostic any other caller would
    /// observe, never a fabricated in-process check.
    /// </summary>
    public CommandResult ConfirmHostControlUnavailable(string role, TimeSpan? timeout = null) =>
        myWorkspace.RunBackendSpecSquadHq(
            [
                "wait-for-agent",
                role,
                "--timeout",
                (timeout ?? TimeSpan.FromSeconds(2)).TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                myWorkspace.Root,
            ]);

    /// <summary>
    /// Writes a durable marker file into the given role's own worktree before the process starts, so
    /// <see cref="DurableRoleFileIsPreserved"/> can later prove a normal shutdown neither deletes nor corrupts
    /// durable workspace content the launched process does not itself own.
    /// </summary>
    public void SeedDurableRoleFile(string role, string relativePath, string content) =>
        myWorkspace.WriteFileInRoleWorktree(role, relativePath, content);

    /// <summary>Whether the given role's durable marker file (see <see cref="SeedDurableRoleFile"/>) still exists
    /// on disk and still contains its original content.</summary>
    public bool DurableRoleFileIsPreserved(string role, string relativePath, string expectedContent)
    {
        var path = Path.Combine(myWorkspace.RoleWorktreePath(role), Path.Combine(relativePath.Split('/')));
        return File.Exists(path) && File.ReadAllText(path).Contains(expectedContent, StringComparison.Ordinal);
    }

    /// <summary>
    /// Poisons the given role's handoff outbox directory (see <see cref="ScenarioWorkspace.PoisonRoleHandoffOutbox"/>)
    /// so the next real poll by the launched process's own handoff poller genuinely faults - a deterministic, real
    /// filesystem fault, never an injected pump failure.
    /// </summary>
    public void PoisonHandoffOutbox(string role) => myWorkspace.PoisonRoleHandoffOutbox(role);

    /// <summary>
    /// Repairs the given role's handoff outbox directory (see <see cref="ScenarioWorkspace.RepairRoleHandoffOutbox"/>)
    /// after <see cref="PoisonHandoffOutbox"/>, standing in for the real-world remediation an operator would perform
    /// before a subsequent process could launch healthily against the same workspace.
    /// </summary>
    public void RepairHandoffOutbox(string role) => myWorkspace.RepairRoleHandoffOutbox(role);

    /// <summary>
    /// Configures the next <see cref="StartAsync{TProviderFactory}"/> launch's fake provider to pause
    /// immediately before creating the given number of sessions - for example 0 pauses before the very first
    /// role's session, 1 pauses after the first role's session has started and been notified across the control
    /// pipe, but before the next role's - so a specification can prove a host-control shutdown requested while
    /// provider startup is genuinely paused there still disposes every session already registered and terminates
    /// cleanly. Requires <see cref="EnableFakeProviderControl"/> to have also been called, so the paused position
    /// is independently observable across the control pipe rather than merely inferred from timing.
    /// </summary>
    public void GateProviderStartupAfterSessions(int count) => myStartupGateAfterSessions = count;

    /// <summary>
    /// Configures the next launch's fake provider to fail before its runtime becomes available at all - before any
    /// session could possibly be created or connected across the control pipe - mirroring a real provider whose
    /// runtime never becomes available. Must be launched through <see cref="LaunchWithoutReadyHandshake{TProviderFactory}"/>,
    /// since readiness is never reached.
    /// </summary>
    public void FailProviderBeforeRuntime() => myFailProviderBeforeRuntime = true;

    /// <summary>
    /// Configures the next launch's fake provider to fail immediately after starting (and notifying the control
    /// pipe about) the given number of sessions, mirroring a real provider whose runtime throws partway through
    /// establishing role sessions. Must be launched through
    /// <see cref="LaunchWithoutReadyHandshake{TProviderFactory}"/>, since readiness is never reached. Requires
    /// <see cref="EnableFakeProviderControl"/> to have also been called, so the already-started session(s) and the
    /// never-started remainder are both independently observable across the control pipe.
    /// </summary>
    public void FailProviderAfterSessions(int count) => myFailProviderAfterSessions = count;

    /// <summary>
    /// Configures the next launch's fake provider runtime to fail its own disposal with the given message, after
    /// every session it started has genuinely already been disposed - mirroring a real provider runtime whose
    /// final retirement step independently fails even though every session it owned was properly torn down
    /// first. Set before launch, so it can be paired deterministically with any independent primary startup or
    /// runtime failure without racing that failure's own timing.
    /// </summary>
    public void FailProviderDisposal(string message) => myFailProviderDisposalMessage = message;

    /// <summary>
    /// Requests shutdown through the real "squad-hq shutdown" host-control command as soon as it is reachable at
    /// all, retrying the request until it lands - since the launched process's host-control endpoint may not yet
    /// be listening in the very first instant after the process starts - and awaits the same clean exit
    /// <see cref="ShutdownAsync"/> does, returning the exit code observed. Proves shutdown wins even when
    /// requested at the earliest possible moment, racing host-lease acquisition and the wait for "ui.ready"
    /// itself rather than deliberately waiting for any later, more convenient point. Only one attempt is ever in
    /// flight at a time - each retry waits for its own attempt process to finish before deciding whether another
    /// is needed - so a slow first attempt never causes a pile-up of overlapping "squad-hq shutdown" child
    /// processes. The very first attempt is issued as a background process rather than awaited synchronously, so
    /// - immediately after it is issued, before the launched process has necessarily terminated - this can also
    /// deliver a UI-protocol prompt for <paramref name="sendPromptToRole"/> (when supplied), proving a command
    /// sent once shutdown has already been requested never reaches a provider session. A broken-pipe failure from
    /// sending that prompt after the process has already fully exited is swallowed, since that failure is itself
    /// further proof no session ever received it.
    /// </summary>
    public async Task<int> RequestShutdownAsSoonAsReachableAsync(
        TimeSpan? timeout = null, string? sendPromptToRole = null, string? promptContent = null)
    {
        if (myProcess is null || myUi is null)
        {
            throw new InvalidOperationException("The backend process has not been started.");
        }

        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        var firstAttemptIssued = false;
        while (true)
        {
            var attempt = myWorkspace.StartProcess(myWorkspace.BackendSpecSquadHqExecutablePath, ["shutdown", myWorkspace.Root]);
            if (!firstAttemptIssued)
            {
                firstAttemptIssued = true;
                if (sendPromptToRole is not null)
                {
                    try
                    {
                        myUi.SendPrompt(sendPromptToRole, promptContent!);
                    }
                    catch (IOException)
                    {
                        // The process had already fully exited by the time this prompt was sent - itself further
                        // proof no session ever received it.
                    }
                }
            }

            var remaining = deadline - DateTime.UtcNow;
            if (remaining > TimeSpan.Zero)
            {
                await WaitForEitherExitAsync(attempt, myProcess, remaining);
            }
            if (myProcess.WaitForExit(0))
            {
                break;
            }
            if (DateTime.UtcNow >= deadline)
            {
                throw new HeadlessUiWaitTimeoutException(
                    "the backend process to exit after repeatedly requesting shutdown",
                    myUi.DescribeDiagnostics(DescribeControlDiagnostics()));
            }
            // This attempt's own process has already finished (most likely because the host-control endpoint
            // was not reachable yet) but the launched process is still running - retry with a fresh attempt.
        }

        return myProcess.ExitCode;
    }

    /// <summary>Waits until either process exits or the given timeout elapses, whichever comes first.</summary>
    private static async Task WaitForEitherExitAsync(System.Diagnostics.Process first, System.Diagnostics.Process second, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            await Task.WhenAny(first.WaitForExitAsync(cancellation.Token), second.WaitForExitAsync(cancellation.Token));
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// Requests shutdown through the real "squad-hq shutdown" host-control command exactly like
    /// <see cref="ShutdownAsync"/>, but issues the request as its own background process instead of awaiting it,
    /// sends the given role a UI-protocol prompt immediately afterward - before termination has necessarily
    /// finished - then awaits the launched process's own clean exit, so a specification can prove a command sent
    /// once shutdown is already in flight never reaches a provider session.
    /// </summary>
    public Task<int> ShutdownWhileSendingPromptAsync(string role, string prompt, TimeSpan? timeout = null)
    {
        if (myProcess is null || myUi is null)
        {
            throw new InvalidOperationException("The backend process has not been started.");
        }

        myWorkspace.StartProcess(myWorkspace.BackendSpecSquadHqExecutablePath, ["shutdown", myWorkspace.Root]);
        try
        {
            myUi.SendPrompt(role, prompt);
        }
        catch (IOException)
        {
            // The process had already fully exited by the time this prompt was sent - itself further proof no
            // session ever received it.
        }

        var deadlineMilliseconds = (int)(timeout ?? DefaultTimeout).TotalMilliseconds;
        if (!myProcess.WaitForExit(deadlineMilliseconds))
        {
            throw new HeadlessUiWaitTimeoutException(
                "the backend process to exit after requesting shutdown while sending a prompt",
                myUi.DescribeDiagnostics(DescribeControlDiagnostics()));
        }

        return Task.FromResult(myProcess.ExitCode);
    }

    /// <summary>
    /// Whether the fake provider has never reported, across the control pipe, that the given role's session
    /// started - a snapshot read (no waiting) proving a not-yet-configured session was genuinely never created,
    /// not merely that it has not yet been observed. Requires <see cref="EnableFakeProviderControl"/>.
    /// </summary>
    public bool RoleSessionNeverStarted(string role) => !RequireControl().HasSessionStarted(role);

    /// <summary>
    /// Starts the real "squad-hq wait-for-agent" command for the given role as a concurrent background probe
    /// racing this scenario's own subsequent shutdown request, so a specification can later prove the role never
    /// reported ready to a live, independently polling caller - not merely that the host became unavailable after
    /// the fact.
    /// </summary>
    public BackendScenarioCommand StartWatchingForReadiness(string role, TimeSpan? timeout = null) =>
        StartWaitForAgent(role, timeout ?? DefaultTimeout);

    /// <summary>
    /// Starts a brand-new <see cref="BackendScenario"/> against this exact same workspace, proving a healthy
    /// replacement process can acquire the same project and reach readiness after this scenario's own process
    /// released it - for example after a normal host-controlled shutdown. The caller owns the returned scenario's
    /// lifetime exactly like this one; it is not disposed automatically by this scenario.
    /// </summary>
    public async Task<BackendScenario> StartReplacementAsync<TProviderFactory>(TimeSpan? timeout = null)
        where TProviderFactory : squad.AgentProvider.Abstractions.IAgentProviderFactory
    {
        var replacement = new BackendScenario(myWorkspace);
        await replacement.StartAsync<TProviderFactory>(timeout);
        return replacement;
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
