using System.Text.Json;
using squad.Specs.Support;

namespace squad.Specs.StepDefinitions;

/// <summary>
/// Launches two independent squad-hq stdio processes against two different temporary project roots (both nested
/// under the same scenario workspace so cleanup remains scenario-scoped) to prove that stopping or terminating one
/// instance never disturbs the other. Deliberately narrow and self-contained per issue 011 slice 4; a reusable
/// scenario facade shared with <see cref="StdioUiProtocolSteps"/> is left to issue 012.
/// </summary>
[Binding]
public sealed class HostCoexistenceSteps
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private readonly ScenarioWorkspace myWorkspace;
    private readonly Dictionary<string, ProjectFixture> myProjects = new(StringComparer.Ordinal);

    public HostCoexistenceSteps(ScenarioWorkspace workspace)
    {
        myWorkspace = workspace;
    }

    [Given("independent squad projects {string} and {string} using the echo provider fixture")]
    public void GivenIndependentSquadProjectsUsingTheEchoProviderFixture(string firstLabel, string secondLabel)
    {
        CreateProject(firstLabel);
        CreateProject(secondLabel);
    }

    [When("squad-hq is launched with \"--ui stdio\" for project {string}")]
    public void WhenSquadHqIsLaunchedWithUiStdioForProject(string label) => Launch(myProjects[label]);

    [When("the ui sends \"ui.ready\" to project {string}")]
    public void WhenTheUiSendsUiReadyToProject(string label) => SendEnvelope(myProjects[label], "ui.ready");

    [When("the ui sends a {string} command for role {string} with prompt {string} to project {string}")]
    public void WhenTheUiSendsACommandForRoleWithPromptToProject(string type, string role, string prompt, string label) =>
        SendEnvelope(myProjects[label], type, role, new { prompt });

    [Then("a \"transcript.update\" message for role {string} with content {string} is written to stdout for project {string}")]
    public void ThenATranscriptUpdateMessageForRoleWithContentIsWrittenToStdoutForProject(string role, string content, string label) =>
        WaitForMessage(
            myProjects[label],
            element => IsType(element, "transcript.update")
                && PayloadRoleEquals(element, role)
                && GetPayload(element).TryGetProperty("entry", out var entry)
                && entry.ValueKind == JsonValueKind.Object
                && entry.TryGetProperty("content", out var contentElement)
                && contentElement.GetString() == content,
            $"a transcript.update message for role '{role}' with content '{content}' on project '{label}'");

    [When("squad-hq requests shutdown for project {string}")]
    public void WhenSquadHqRequestsShutdownForProject(string label) =>
        myWorkspace.RunTool("squad-hq", ["shutdown", myProjects[label].Root]);

    [Then("the shutdown request succeeds for project {string}")]
    public void ThenTheShutdownRequestSucceedsForProject(string label) =>
        Assert.That(myWorkspace.LastResult?.ExitCode, Is.Zero);

    [Then("the squad-hq process for project {string} exits with code {string}")]
    public void ThenTheSquadHqProcessForProjectExitsWithCode(string label, string expectedExitCode)
    {
        var fixture = myProjects[label];
        Assert.That(
            fixture.Process!.WaitForExit((int)DefaultTimeout.TotalMilliseconds),
            Is.True,
            $"Timed out waiting for project '{label}' to exit.");
        Assert.That(fixture.Process.ExitCode, Is.EqualTo(int.Parse(expectedExitCode)));
    }

    [Then("the squad-hq process for project {string} is still running")]
    public void ThenTheSquadHqProcessForProjectIsStillRunning(string label)
    {
        Thread.Sleep(200);
        Assert.That(myProjects[label].Process!.HasExited, Is.False, $"Project '{label}' should still be running.");
    }

    [AfterScenario]
    public void StopProcesses()
    {
        foreach (var fixture in myProjects.Values)
        {
            if (fixture.Process is { HasExited: false })
            {
                fixture.Process.Kill(entireProcessTree: true);
            }
        }
    }

    private void CreateProject(string label)
    {
        var root = myWorkspace.PathInWorkspace("projects", label);
        Directory.CreateDirectory(root);
        RunGit(root, "init", "--quiet");
        RunGit(root, "config", "user.name", "BlaXquad Acceptance");
        RunGit(root, "config", "user.email", "acceptance@example.invalid");
        File.WriteAllText(Path.Combine(root, "README.md"), "# Acceptance fixture\n");
        RunGit(root, "add", ".");
        RunGit(root, "commit", "--quiet", "-m", "Initial fixture");

        Directory.CreateDirectory(Path.Combine(root, "blaxquad", "roles"));
        File.WriteAllText(Path.Combine(root, "blaxquad", "constitution.prompt"), "Follow the project constitution.\n");
        File.WriteAllText(
            Path.Combine(root, "blaxquad", "squad.json"),
            """
            {
              "roles": [
                { "name": "coder", "worktree": "master", "agent": {} }
              ]
            }
            """ + "\n");
        File.WriteAllText(Path.Combine(root, "blaxquad", "roles", "coder.prompt"), "Act as the coder.\n");

        myProjects[label] = new ProjectFixture(root);
    }

    private void RunGit(string root, params string[] arguments)
    {
        var result = myWorkspace.Run("git", arguments, workingDirectory: root);
        Assert.That(result.ExitCode, Is.Zero, () => result.StdErr);
    }

    private void Launch(ProjectFixture fixture)
    {
        var descriptor = $"{typeof(EchoAgentProviderFactory).Assembly.Location};{typeof(EchoAgentProviderFactory).FullName}";
        fixture.Process = myWorkspace.StartTool(
            "squad-hq",
            ["launch", "--provider", descriptor, "--ui", "stdio", fixture.Root],
            environment: null,
            redirectStandardInput: true);
        StartReader(fixture.Process.StandardOutput, fixture.StdOutLines, fixture.LinesLock);
        StartReader(fixture.Process.StandardError, fixture.StdErrLines, fixture.LinesLock);
    }

    private static void StartReader(StreamReader reader, List<string> destination, object linesLock) =>
        Task.Run(async () =>
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                lock (linesLock)
                {
                    destination.Add(line);
                }
            }
        });

    private void SendEnvelope(ProjectFixture fixture, string type, string? role = null, object? payload = null)
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
        fixture.Process!.StandardInput.WriteLine(JsonSerializer.Serialize(envelope));
        fixture.Process.StandardInput.Flush();
    }

    private static JsonElement WaitForMessage(ProjectFixture fixture, Func<JsonElement, bool> predicate, string description)
    {
        var deadline = DateTime.UtcNow + DefaultTimeout;
        while (true)
        {
            List<string> lines;
            lock (fixture.LinesLock)
            {
                lines = [.. fixture.StdOutLines];
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

    private sealed class ProjectFixture(string root)
    {
        public string Root { get; } = root;
        public System.Diagnostics.Process? Process { get; set; }
        public List<string> StdOutLines { get; } = [];
        public List<string> StdErrLines { get; } = [];
        public object LinesLock { get; } = new();
    }
}
