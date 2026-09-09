using System.Text.Json;
using squad.Specs.Support;

namespace squad.Specs.StepDefinitions;

/// <summary>
/// Drives the real, separately launched "squad-hq --ui stdio" process over its actual standard input and standard
/// output, using the minimal <see cref="EchoAgentProviderFactory"/> fixture. A single background reader task per
/// stream continuously drains lines into a locked buffer so step assertions only ever poll that buffer instead of
/// issuing overlapping reads on the process streams.
/// </summary>
[Binding]
public sealed class StdioUiProtocolSteps
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan myPreReadyGraceWindow = TimeSpan.FromSeconds(2);

    private readonly ScenarioWorkspace myWorkspace;
    private readonly object myLinesLock = new();
    private readonly List<string> myStdOutLines = [];
    private readonly List<string> myStdErrLines = [];
    private string myRole = "coder";
    private System.Diagnostics.Process? myProcess;

    public StdioUiProtocolSteps(ScenarioWorkspace workspace)
    {
        myWorkspace = workspace;
    }

    [Given("a git project configured with a {string} role using the echo provider fixture")]
    public void GivenAGitProjectConfiguredWithARoleUsingTheEchoProviderFixture(string role)
    {
        myRole = role;
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

    [Given("a git project configured with {string} and {string} roles using the echo provider fixture")]
    public void GivenAGitProjectConfiguredWithRolesUsingTheEchoProviderFixture(string firstRole, string secondRole)
    {
        myRole = firstRole;
        myWorkspace.InitializeGitRepository();
        myWorkspace.WriteFile("blaxquad/constitution.prompt", "Follow the project constitution.\n");
        myWorkspace.WriteFile(
            "blaxquad/squad.json",
            $$"""
            {
              "roles": [
                { "name": "{{firstRole}}", "worktree": "master", "agent": {} },
                { "name": "{{secondRole}}", "worktree": "{{secondRole}}", "agent": {} }
              ]
            }
            """ + "\n");
        myWorkspace.WriteFile($"blaxquad/roles/{firstRole}.prompt", $"Act as the {firstRole}.\n");
        myWorkspace.WriteFile($"blaxquad/roles/{secondRole}.prompt", $"Act as the {secondRole}.\n");
    }

    [When("squad-hq is launched with \"--ui stdio\"")]
    public void WhenSquadHqIsLaunchedWithUiStdio() => Launch();

    [When("squad-hq requests shutdown for the workspace")]
    public void WhenSquadHqRequestsShutdownForTheWorkspace() =>
        myWorkspace.RunTool("squad-hq", ["shutdown", myWorkspace.Root]);

    [Then("the shutdown request succeeds")]
    public void ThenTheShutdownRequestSucceeds() =>
        Assert.That(myWorkspace.LastResult?.ExitCode, Is.Zero);

    [When("the ui sends \"ui.ready\"")]
    public void WhenTheUiSendsUiReady() => SendEnvelope("ui.ready");

    [When("the ui sends a {string} command for role {string} with prompt {string}")]
    public void WhenTheUiSendsACommandForRoleWithPrompt(string type, string role, string prompt) =>
        SendEnvelope(type, role, new { prompt });

    [When("the ui requests a transcript page for role {string} before index {int}")]
    public void WhenTheUiRequestsATranscriptPageForRoleBeforeIndex(string role, int beforeIndex) =>
        SendEnvelope("transcript.page", role, new { beforeIndex });

    [When("the ui requests transcript synchronization")]
    public void WhenTheUiRequestsTranscriptSynchronization() => SendEnvelope("transcript.synchronize");

    [When("the ui sends the malformed line {string}")]
    public void WhenTheUiSendsTheMalformedLine(string line) => SendRawLine(line);

    [When("the squad-hq process is forcibly terminated")]
    public void WhenTheSquadHqProcessIsForciblyTerminated() => myProcess!.Kill(entireProcessTree: true);

    [Then("the squad-hq process has exited")]
    public void ThenTheSquadHqProcessHasExited() =>
        Assert.That(myProcess!.WaitForExit((int)DefaultTimeout.TotalMilliseconds), Is.True, "Timed out waiting for the squad-hq process to exit.");

    [Then("no protocol message is written to stdout yet")]
    public void ThenNoProtocolMessageIsWrittenToStdoutYet()
    {
        // A single fixed-delay check can pass trivially if launch preparation (workspace/provider/sleep-inhibitor
        // setup) is still running when it fires, proving nothing about the ui.ready gate. Poll continuously across
        // a bounded window generous enough to span that preparation instead, and fail the instant any line appears.
        var deadline = DateTime.UtcNow + myPreReadyGraceWindow;
        while (DateTime.UtcNow < deadline)
        {
            lock (myLinesLock)
            {
                Assert.That(myStdOutLines, Is.Empty, "Protocol output appeared before \"ui.ready\" was sent.");
            }
            Thread.Sleep(25);
        }
    }

    [Then("an initial \"transcript.synchronize\" message for role {string} is written to stdout")]
    public void ThenAnInitialTranscriptSynchronizeMessageForRoleIsWrittenToStdout(string role) =>
        WaitForMessage(
            element => IsType(element, "transcript.synchronize")
                && GetPayload(element).GetProperty("recovery").GetBoolean() == false
                && PayloadRolesContain(GetPayload(element), role),
            $"an initial transcript.synchronize message for role '{role}'");

    [Then("a recovery \"transcript.synchronize\" message for role {string} is written to stdout")]
    public void ThenARecoveryTranscriptSynchronizeMessageForRoleIsWrittenToStdout(string role) =>
        WaitForMessage(
            element => IsType(element, "transcript.synchronize")
                && GetPayload(element).GetProperty("recovery").GetBoolean()
                && PayloadRolesContain(GetPayload(element), role),
            $"a recovery transcript.synchronize message for role '{role}'");

    [Then("a \"state.snapshot\" message is written to stdout")]
    public void ThenAStateSnapshotMessageIsWrittenToStdout() =>
        WaitForMessage(element => IsType(element, "state.snapshot"), "a state.snapshot message");

    [Then("a \"transcript.update\" message for role {string} with content {string} is written to stdout")]
    public void ThenATranscriptUpdateMessageForRoleWithContentIsWrittenToStdout(string role, string content) =>
        WaitForMessage(
            element => IsType(element, "transcript.update")
                && PayloadRoleEquals(element, role)
                && GetPayload(element).TryGetProperty("entry", out var entry)
                && entry.ValueKind == JsonValueKind.Object
                && entry.TryGetProperty("content", out var contentElement)
                && contentElement.GetString() == content,
            $"a transcript.update message for role '{role}' with content '{content}'");

    [Then("a \"transcript.page\" message for role {string} is written to stdout")]
    public void ThenATranscriptPageMessageForRoleIsWrittenToStdout(string role) =>
        WaitForMessage(
            element => IsType(element, "transcript.page") && PayloadRoleEquals(element, role),
            $"a transcript.page message for role '{role}'");

    [Then("a \"protocol.error\" message is written to stdout")]
    public void ThenAProtocolErrorMessageIsWrittenToStdout() =>
        WaitForMessage(element => IsType(element, "protocol.error"), "a protocol.error message");

    [Then("the squad-hq process is still running")]
    public void ThenTheSquadHqProcessIsStillRunning()
    {
        Thread.Sleep(200);
        Assert.That(myProcess!.HasExited, Is.False);
    }

    [Then("the squad-hq process exits with code {string}")]
    public void ThenTheSquadHqProcessExitsWithCode(string expectedExitCode)
    {
        Assert.That(myProcess!.WaitForExit((int)DefaultTimeout.TotalMilliseconds), Is.True, "Timed out waiting for the squad-hq process to exit.");
        Assert.That(myProcess.ExitCode, Is.EqualTo(int.Parse(expectedExitCode)));
    }

    [Then("every stdout line is a well-formed protocol envelope")]
    public void ThenEveryStdoutLineIsAWellFormedProtocolEnvelope()
    {
        WaitForMessage(element => IsType(element, "transcript.update") && PayloadRoleEquals(element, myRole), "a transcript.update message before checking envelope shape");

        List<string> lines;
        lock (myLinesLock)
        {
            lines = [.. myStdOutLines];
        }
        Assert.That(lines, Is.Not.Empty);
        foreach (var line in lines)
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            Assert.Multiple(() =>
            {
                Assert.That(root.TryGetProperty("version", out var version) && version.GetInt32() == 3, Is.True, $"Not a protocol envelope: {line}");
                Assert.That(root.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String, Is.True, $"Not a protocol envelope: {line}");
            });
        }
    }

    [Then("standard error contains no protocol envelope")]
    public void ThenStandardErrorContainsNoProtocolEnvelope()
    {
        List<string> lines;
        lock (myLinesLock)
        {
            lines = [.. myStdErrLines];
        }
        foreach (var line in lines)
        {
            Assert.That(LooksLikeProtocolEnvelope(line), Is.False, $"Unexpected protocol envelope on stderr: {line}");
        }
    }

    [AfterScenario]
    public void StopProcess()
    {
        if (myProcess is { HasExited: false })
        {
            myProcess.Kill(entireProcessTree: true);
        }
    }

    private void Launch()
    {
        // Relaunching against the same workspace within one scenario (e.g. after a forced termination) must not
        // let a Then-step observe stale lines from a previous process instance, so each launch starts with a
        // clean buffer.
        lock (myLinesLock)
        {
            myStdOutLines.Clear();
            myStdErrLines.Clear();
        }
        var descriptor = $"{typeof(EchoAgentProviderFactory).Assembly.Location};{typeof(EchoAgentProviderFactory).FullName}";
        myProcess = myWorkspace.StartTool(
            "squad-hq",
            ["launch", "--provider", descriptor, "--ui", "stdio", myWorkspace.Root],
            environment: null,
            redirectStandardInput: true);
        StartReader(myProcess.StandardOutput, myStdOutLines);
        StartReader(myProcess.StandardError, myStdErrLines);
    }

    private void StartReader(StreamReader reader, List<string> destination) =>
        Task.Run(async () =>
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                lock (myLinesLock)
                {
                    destination.Add(line);
                }
            }
        });

    private void SendEnvelope(string type, string? role = null, object? payload = null)
    {
        var envelope = new Dictionary<string, object?> { ["version"] = 3, ["type"] = type };
        if (role is not null)
        {
            envelope["role"] = role;
        }
        if (payload is not null)
        {
            envelope["payload"] = payload;
        }
        SendRawLine(JsonSerializer.Serialize(envelope));
    }

    private void SendRawLine(string line)
    {
        myProcess!.StandardInput.WriteLine(line);
        myProcess.StandardInput.Flush();
    }

    private JsonElement WaitForMessage(Func<JsonElement, bool> predicate, string description, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (true)
        {
            List<string> lines;
            lock (myLinesLock)
            {
                lines = [.. myStdOutLines];
            }
            foreach (var line in lines)
            {
                using var document = JsonDocument.Parse(line);
                if (predicate(document.RootElement))
                {
                    return document.RootElement.Clone();
                }
            }
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException($"Timed out waiting for {description}. Stdout so far:\n{string.Join('\n', lines)}");
            }
            Thread.Sleep(25);
        }
    }

    private static bool IsType(JsonElement element, string type) =>
        element.TryGetProperty("type", out var typeElement) && typeElement.GetString() == type;

    private static JsonElement GetPayload(JsonElement element) => element.GetProperty("payload");

    private static bool PayloadRoleEquals(JsonElement element, string role) =>
        GetPayload(element).TryGetProperty("role", out var roleElement) && roleElement.GetString() == role;

    private static bool PayloadRolesContain(JsonElement payload, string role) =>
        payload.TryGetProperty("roles", out var roles)
        && roles.EnumerateArray().Any(entry => entry.TryGetProperty("role", out var name) && name.GetString() == role);

    private static bool LooksLikeProtocolEnvelope(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("version", out _)
                && document.RootElement.TryGetProperty("type", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
