using System.Text.Json;
using squad.Specs.Support;

namespace squad.Specs.StepDefinitions;

/// <summary>
/// Drives the fake-provider control transport directly - the private test-runner-to-provider-process pipe added
/// in issue 012's slice 7 - without launching a real squad-hq process. Proves the server's own envelope
/// validation (authentication token, protocol version, correlation id, unknown commands) and that its single
/// dispatch loop lets many concurrent session observations and acknowledgements proceed without competing to read
/// the pipe.
/// </summary>
[Binding]
public sealed class FakeProviderControlProtocolSteps
{
    private static readonly TimeSpan ObservationTimeout = TimeSpan.FromSeconds(5);

    private FakeProviderControlServer? myServer;
    private Task? myConnectionTask;
    private RawControlPipeClient? myRawClient;
    private FakeProviderControlClient? myClient;
    private JsonElement myLastResponse;
    private readonly HashSet<(string Role, string SessionId)> myDisposedSessions = new();
    private string? myLastServerExceptionMessage;

    [Given("a fake-provider control server is listening")]
    public void GivenAFakeProviderControlServerIsListening()
    {
        myServer = FakeProviderControlServer.Create();
        myConnectionTask = myServer.WaitForConnectionAsync();
    }

    [Given("an authenticated fake-provider control client is connected")]
    public void GivenAnAuthenticatedFakeProviderControlClientIsConnected() => Await(ConnectAuthenticatedClientAsync());

    [When("a client connects to the control pipe with an invalid token")]
    public void WhenAClientConnectsToTheControlPipeWithAnInvalidToken() =>
        SendRawAndCapture(new { version = 1, type = "connect", correlationId = NewCorrelationId(), payload = new { token = "not-the-real-token" } });

    [When("a client sends a {string} envelope with protocol version {int}")]
    public void WhenAClientSendsAnEnvelopeWithProtocolVersion(string type, int version) =>
        SendRawAndCapture(new { version, type, correlationId = NewCorrelationId(), payload = new { token = myServer!.Token } });

    [When("a client sends a {string} envelope with no correlation id")]
    public void WhenAClientSendsAnEnvelopeWithNoCorrelationId(string type) =>
        SendRawAndCapture(new { version = 1, type, payload = new { token = myServer!.Token } });

    [When("a client sends an envelope with the unknown command {string}")]
    public void WhenAClientSendsAnEnvelopeWithTheUnknownCommand(string command) =>
        SendRawAndCapture(new { version = 1, type = command, correlationId = NewCorrelationId(), payload = new { } });

    [When("the client concurrently reports {int} session starts and disposals")]
    public void WhenTheClientConcurrentlyReportsSessionStartsAndDisposals(int count) => Await(ReportConcurrentlyAsync(count));

    [Then("the client observes a protocol error mentioning {string}")]
    public void ThenTheClientObservesAProtocolErrorMentioning(string expectedSubstring)
    {
        Assert.That(myLastResponse.GetProperty("type").GetString(), Is.EqualTo("protocol-error"));
        var message = myLastResponse.GetProperty("payload").GetProperty("message").GetString();
        Assert.That(message, Does.Contain(expectedSubstring).IgnoreCase);
    }

    [Then("the server observes all {int} session starts and disposals")]
    public void ThenTheServerObservesAllSessionStartsAndDisposals(int count) => Await(VerifyObservedAsync(count));

    [When("the client reports a session started and then disposed for role {string} and session {string}")]
    public void WhenTheClientReportsASessionStartedAndThenDisposedForRoleAndSession(string role, string sessionId) =>
        Await(ReportStartedThenDisposedAsync(role, sessionId));

    [When("the client reports a session started for role {string} and session {string}")]
    public void WhenTheClientReportsASessionStartedForRoleAndSession(string role, string sessionId) =>
        Await(myClient!.NotifySessionStartedAsync(role, sessionId));

    [Then("the server's undisposed-session diagnostic mentions role {string} and session {string}")]
    public void ThenTheServersUndisposedSessionDiagnosticMentionsRoleAndSession(string role, string sessionId)
    {
        var diagnostic = myServer!.DescribeUndisposedSessions();
        Assert.That(diagnostic, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(diagnostic, Does.Contain(role));
            Assert.That(diagnostic, Does.Contain(sessionId));
        });
    }

    [Then("the server's undisposed-session diagnostic is empty")]
    public void ThenTheServersUndisposedSessionDiagnosticIsEmpty() =>
        Assert.That(myServer!.DescribeUndisposedSessions(), Is.Null);

    [When("the server replies to role {string} with content {string}")]
    public void WhenTheServerRepliesToRoleWithContent(string role, string content) => Await(ReplyAndCaptureAsync(role, content));

    [Then("the server reports a protocol error mentioning {string}")]
    public void ThenTheServerReportsAProtocolErrorMentioning(string expectedSubstring)
    {
        Assert.That(myLastServerExceptionMessage, Is.Not.Null);
        Assert.That(myLastServerExceptionMessage, Does.Contain(expectedSubstring).IgnoreCase);
    }

    [AfterScenario]
    public async Task CleanUpAsync()
    {
        if (myClient is not null)
        {
            await myClient.DisposeAsync();
        }
        if (myRawClient is not null)
        {
            await myRawClient.DisposeAsync();
        }
        if (myServer is not null)
        {
            await myServer.DisposeAsync();
        }
    }

    private async Task ConnectAuthenticatedClientAsync()
    {
        var clientConnecting = FakeProviderControlClient.ConnectAsync(
            myServer!.PipeName, myServer.Token,
            (role, sessionId, _, _) => Task.FromResult<string?>(
                myDisposedSessions.Contains((role, sessionId))
                    ? $"Session '{sessionId}' for role '{role}' has been disposed."
                    : null),
            (_, _, _, _, _) => Task.FromResult<string?>(null));
        await myConnectionTask!;
        myClient = await clientConnecting;
    }

    private async Task ReportStartedThenDisposedAsync(string role, string sessionId)
    {
        await myClient!.NotifySessionStartedAsync(role, sessionId);
        await myClient!.NotifySessionDisposedAsync(role, sessionId);
        myDisposedSessions.Add((role, sessionId));
    }

    private async Task ReplyAndCaptureAsync(string role, string content)
    {
        myLastServerExceptionMessage = null;
        try
        {
            await myServer!.ReplyAsync(role, content, ObservationTimeout);
        }
        catch (FakeProviderControlProtocolException exception)
        {
            myLastServerExceptionMessage = exception.Message;
        }
    }

    private async Task ReportConcurrentlyAsync(int count)
    {
        var tasks = new List<Task>();
        for (var index = 0; index < count; index++)
        {
            var role = $"role-{index}";
            var sessionId = Guid.NewGuid().ToString("n");
            tasks.Add(myClient!.NotifySessionStartedAsync(role, sessionId));
            tasks.Add(myClient!.NotifySessionDisposedAsync(role, sessionId));
        }
        await Task.WhenAll(tasks);
    }

    private async Task VerifyObservedAsync(int count)
    {
        for (var index = 0; index < count; index++)
        {
            var role = $"role-{index}";
            await myServer!.WaitForSessionStartedAsync(role, ObservationTimeout);
            await myServer!.WaitForSessionDisposedAsync(role, ObservationTimeout);
        }
    }

    private void SendRawAndCapture(object envelope) => Await(SendRawAndCaptureAsync(envelope));

    private async Task SendRawAndCaptureAsync(object envelope)
    {
        myRawClient = await RawControlPipeClient.ConnectAsync(myServer!.PipeName);
        await myConnectionTask!;
        await myRawClient.SendRawLineAsync(JsonSerializer.Serialize(envelope));
        myLastResponse = await myRawClient.ReadResponseAsync();
    }

    private static string NewCorrelationId() => Guid.NewGuid().ToString("n");

    private static void Await(Task task) => task.GetAwaiter().GetResult();
}
