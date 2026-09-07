using System.Text.Json;
using squad.Specs.Support;

namespace squad.Specs.StepDefinitions;

/// <summary>
/// Drives the real, separately launched "squad-hq --ui stdio" process using the <see
/// cref="ControllableAgentProviderFactory"/> fixture and its test-owned <see cref="UsageControlServer"/> control
/// pipe. Proves the process/protocol boundary contract for the Copilot adapter's active usage refresh policy:
/// provider-reported context and AIC usage received while a role is still working reaches the real UI JSON
/// protocol before idle, and the final snapshot after idle preserves the latest reported values.
/// </summary>
[Binding]
public sealed class ActiveUsageRefreshSteps
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private readonly ScenarioWorkspace myWorkspace;
    private readonly object myLinesLock = new();
    private readonly List<string> myStdOutLines = [];
    private System.Diagnostics.Process? myProcess;
    private UsageControlServer? myControl;
    private Task? myControlConnected;

    public ActiveUsageRefreshSteps(ScenarioWorkspace workspace)
    {
        myWorkspace = workspace;
    }

    [Given("a git project configured with a {string} role using the controllable provider fixture")]
    public void GivenAGitProjectConfiguredWithARoleUsingTheControllableProviderFixture(string role)
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

    [When("squad-hq is launched with \"--ui stdio\" using the controllable provider fixture")]
    public void WhenSquadHqIsLaunchedWithUiStdioUsingTheControllableProviderFixture()
    {
        myControl = UsageControlServer.Create();
        myControlConnected = myControl.WaitForConnectionAsync(CancellationToken.None);

        var descriptor = $"{typeof(ControllableAgentProviderFactory).Assembly.Location};{typeof(ControllableAgentProviderFactory).FullName}";
        myProcess = myWorkspace.StartTool(
            "squad-hq",
            ["launch", "--provider", descriptor, "--ui", "stdio", myWorkspace.Root],
            environment: new Dictionary<string, string?>
            {
                [ControllableAgentProviderFactory.PipeNameEnvironmentVariable] = myControl.PipeName,
            },
            redirectStandardInput: true);
        StartReader(myProcess.StandardOutput, myStdOutLines);
        StartReader(myProcess.StandardError, []);
    }

    [When("the ui completes the ready handshake")]
    public void WhenTheUiCompletesTheReadyHandshake() => SendEnvelope("ui.ready");

    [When("the controllable provider session for role {string} has started")]
    public void WhenTheControllableProviderSessionForRoleHasStarted(string role)
    {
        WaitFor(async cancellationToken =>
        {
            await myControlConnected!.WaitAsync(cancellationToken);
            await myControl!.WaitForSessionStartedAsync(role, cancellationToken);
        }, $"the controllable provider session for role '{role}' to start");
    }

    [When("the ui sends prompt {string} to role {string}")]
    public void WhenTheUiSendsPromptToRole(string prompt, string role) =>
        SendEnvelope("prompt.send", role, new { prompt });

    [When("the controllable provider reports context usage {int} of {int} and AIC usage {decimal} for role {string}")]
    public void WhenTheControllableProviderReportsUsageForRole(int contextUsed, int contextLimit, decimal aicUsed, string role) =>
        WaitFor(cancellationToken => myControl!.SendUsageAsync(role, contextUsed, contextLimit, aicUsed, cancellationToken),
            $"the controllable provider to report usage for role '{role}'");

    [When("the controllable provider goes idle for role {string} with context usage {int} of {int} and AIC usage {decimal}")]
    public void WhenTheControllableProviderGoesIdleForRole(string role, int contextUsed, int contextLimit, decimal aicUsed) =>
        WaitFor(cancellationToken => myControl!.SendIdleAsync(role, contextUsed, contextLimit, aicUsed, cancellationToken),
            $"the controllable provider to go idle for role '{role}'");

    [Then("a \"state.snapshot\" message reports role {string} as working with context usage {int} of {int} and AIC usage {decimal}")]
    public void ThenAStateSnapshotMessageReportsRoleAsWorkingWithUsage(string role, int contextUsed, int contextLimit, decimal aicUsed) =>
        WaitForRoleSnapshot(role, isWorking: true, contextUsed, contextLimit, aicUsed);

    [Then("a \"state.snapshot\" message reports role {string} as idle with context usage {int} of {int} and AIC usage {decimal}")]
    public void ThenAStateSnapshotMessageReportsRoleAsIdleWithUsage(string role, int contextUsed, int contextLimit, decimal aicUsed) =>
        WaitForRoleSnapshot(role, isWorking: false, contextUsed, contextLimit, aicUsed);

    [AfterScenario]
    public async Task StopProcessAsync()
    {
        if (myProcess is { HasExited: false })
        {
            myProcess.Kill(entireProcessTree: true);
        }
        if (myControl is not null)
        {
            await myControl.DisposeAsync();
        }
    }

    private void WaitForRoleSnapshot(string role, bool isWorking, int contextUsed, int contextLimit, decimal aicUsed)
    {
        var deadline = DateTime.UtcNow + DefaultTimeout;
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
                if (MatchesRoleSnapshot(document.RootElement, role, isWorking, contextUsed, contextLimit, aicUsed))
                {
                    return;
                }
            }
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"Timed out waiting for a state.snapshot reporting role '{role}' as {(isWorking ? "working" : "idle")} " +
                    $"with context usage {contextUsed} of {contextLimit} and AIC usage {aicUsed}. Stdout so far:\n{string.Join('\n', lines)}");
            }
            Thread.Sleep(25);
        }
    }

    private static bool MatchesRoleSnapshot(JsonElement element, string role, bool isWorking, int contextUsed, int contextLimit, decimal aicUsed)
    {
        if (!element.TryGetProperty("type", out var type) || type.GetString() != "state.snapshot")
        {
            return false;
        }
        var payload = element.GetProperty("payload");
        if (!payload.TryGetProperty("roles", out var roles))
        {
            return false;
        }
        foreach (var roleElement in roles.EnumerateArray())
        {
            if (!roleElement.TryGetProperty("role", out var roleName) || roleName.GetString() != role)
            {
                continue;
            }
            if (roleElement.GetProperty("isWorking").GetBoolean() != isWorking)
            {
                continue;
            }
            if (!roleElement.TryGetProperty("contextUsedTokens", out var used) || used.ValueKind == JsonValueKind.Null || used.GetInt64() != contextUsed)
            {
                continue;
            }
            if (!roleElement.TryGetProperty("contextLimitTokens", out var limit) || limit.ValueKind == JsonValueKind.Null || limit.GetInt64() != contextLimit)
            {
                continue;
            }
            if (!roleElement.TryGetProperty("aicUsed", out var aic) || aic.ValueKind == JsonValueKind.Null || aic.GetDecimal() != aicUsed)
            {
                continue;
            }
            return true;
        }
        return false;
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
        myProcess!.StandardInput.WriteLine(JsonSerializer.Serialize(envelope));
        myProcess.StandardInput.Flush();
    }

    private static void WaitFor(Func<CancellationToken, Task> action, string description)
    {
        using var cancellation = new CancellationTokenSource(DefaultTimeout);
        try
        {
            action(cancellation.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            throw new TimeoutException($"Timed out waiting for {description}.");
        }
    }
}
