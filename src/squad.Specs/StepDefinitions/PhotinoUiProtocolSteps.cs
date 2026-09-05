using System.Collections.Concurrent;
using System.Text.Json;
using squad.Photino;
using squad.Specs.Support;

namespace squad.Specs.StepDefinitions;

[Binding]
public sealed class PhotinoUiProtocolSteps : IAsyncDisposable
{
    private readonly RecordingPhotinoUi myUi = new();
    private readonly QueuedSynchronizationContext myPublisherContext = new();
    private readonly ConcurrentQueue<string> mySerializedMessages = new();
    private readonly ConcurrentQueue<string> mySerializedMessageAttempts = new();
    private PhotinoWindowHost? myHost;
    private JsonElement myLastSerializedMessage;
    private string? myExpectedProtocolError;
    private Task? myPendingReceive;
    private string? mySerializedMessageFailure;
    private int mySerializedMessageFailuresRemaining;

    [Given("a recording Photino protocol host")]
    public void GivenARecordingPhotinoProtocolHost()
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(myPublisherContext);
        try
        {
            myHost = new(
                myUi,
                Environment.CurrentDirectory,
                uiDirectory: null,
                myUi.RecordOpenedUrl,
                RecordSerializedMessage);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [When("the UI sends its readiness command")]
    public async Task WhenTheUiSendsItsReadinessCommand()
    {
        ResetRecordings();
        await ReceiveAsync("""{"version":3,"type":"ui.ready"}""");
        myPublisherContext.Drain();
        myLastSerializedMessage =
            LastSerializedMessage("transcript.synchronize");
    }

    [Then("UI readiness is signaled")]
    public void ThenUiReadinessIsSignaled() =>
        Assert.That(Host.UiReady.IsCompletedSuccessfully, Is.True);

    [Then("the initial transcript high-water mark is requested")]
    public void ThenTheInitialTranscriptHighWaterMarkIsRequested() =>
        AssertCalls(
            "transcript.snapshot:1",
            "transcript.snapshot:500",
            "snapshot");

    [Then("an initial transcript synchronization is serialized")]
    public void ThenAnInitialTranscriptSynchronizationIsSerialized()
    {
        AssertSerializedTypes("state.snapshot", "transcript.synchronize");
        Assert.That(
            myLastSerializedMessage.GetProperty("payload")
                .GetProperty("recovery").GetBoolean(),
            Is.False);
    }

    [When("the UI requests transcript synchronization")]
    public async Task WhenTheUiRequestsTranscriptSynchronization()
    {
        ResetRecordings();
        await ReceiveAsync(
            """
            {
              "version": 3,
              "type": "transcript.synchronize",
              "payload": {
                "roles": [{
                  "role": "coder",
                  "visualSequence": 3,
                  "announcementSequence": 2
                }]
              }
            }
            """);
        myPublisherContext.Drain();
        myLastSerializedMessage =
            LastSerializedMessage("transcript.synchronize");
    }

    [Then("a recovery transcript synchronization is serialized")]
    public void ThenARecoveryTranscriptSynchronizationIsSerialized()
    {
        AssertCalls("transcript.snapshot:500", "snapshot");
        AssertSerializedTypes("state.snapshot", "transcript.synchronize");
        Assert.That(
            myLastSerializedMessage.GetProperty("payload")
                .GetProperty("recovery").GetBoolean(),
            Is.True);
    }

    [When("the UI requests a transcript page")]
    public async Task WhenTheUiRequestsATranscriptPage()
    {
        ResetRecordings();
        await ReceiveAsync(
            """
            {
              "version": 3,
              "type": "transcript.page",
              "role": "coder",
              "payload": { "beforeIndex": 5 }
            }
            """);
        myLastSerializedMessage = LastSerializedMessage("transcript.page");
    }

    [Then("the requested transcript page is serialized")]
    public void ThenTheRequestedTranscriptPageIsSerialized()
    {
        var payload = myLastSerializedMessage.GetProperty("payload");
        AssertCalls("transcript.page:coder:5:200");
        AssertSerializedTypes("transcript.page");
        Assert.Multiple(() =>
        {
            Assert.That(
                payload.GetProperty("role").GetString(),
                Is.EqualTo("coder"));
            Assert.That(
                payload.GetProperty("entries")[0]
                    .GetProperty("entryIndex").GetInt32(),
                Is.EqualTo(4));
        });
    }

    [When("the UI requests an archived transcript entry")]
    public async Task WhenTheUiRequestsAnArchivedTranscriptEntry()
    {
        ResetRecordings();
        await ReceiveAsync(
            """
            {
              "version": 3,
              "type": "transcript.entry",
              "role": "coder",
              "payload": { "entryIndex": 4 }
            }
            """);
        myLastSerializedMessage = LastSerializedMessage("transcript.entry");
    }

    [Then("the requested archived transcript entry is serialized")]
    public void ThenTheRequestedArchivedTranscriptEntryIsSerialized()
    {
        var payload = myLastSerializedMessage.GetProperty("payload");
        AssertCalls("transcript.entry:coder:4");
        AssertSerializedTypes("transcript.entry");
        Assert.Multiple(() =>
        {
            Assert.That(
                payload.GetProperty("entryIndex").GetInt32(),
                Is.EqualTo(4));
            Assert.That(
                payload.GetProperty("entry").GetProperty("content").GetString(),
                Is.EqualTo("archived entry"));
        });
    }

    [When("the UI sends a prompt command")]
    public Task WhenTheUiSendsAPromptCommand() =>
        ReceiveAsync(
            """
            {
              "version": 3,
              "type": "prompt.send",
              "role": "coder",
              "payload": { "prompt": "hello" }
            }
            """);

    [When("the UI sends an abort command")]
    public Task WhenTheUiSendsAnAbortCommand() =>
        ReceiveAsync(
            """{"version":3,"type":"role.abort","role":"coder"}""");

    [When("the UI sends a permission response")]
    public Task WhenTheUiSendsAPermissionResponse() =>
        ReceiveAsync(
            """
            {
              "version": 3,
              "type": "permission.respond",
              "role": "coder",
              "requestId": "permission-1",
              "payload": { "approved": true }
            }
            """);

    [When("the UI sends an input response")]
    public Task WhenTheUiSendsAnInputResponse() =>
        ReceiveAsync(
            """
            {
              "version": 3,
              "type": "input.respond",
              "role": "reviewer",
              "requestId": "input-1",
              "payload": {
                "answer": "typed",
                "wasFreeform": false
              }
            }
            """);

    [When("the UI sends a form elicitation response")]
    public Task WhenTheUiSendsAFormElicitationResponse() =>
        ReceiveAsync(
            """
            {
              "version": 3,
              "type": "elicitation.respond",
              "role": "writer",
              "requestId": "form-1",
              "payload": {
                "action": "accept",
                "content": { "answer": "okay" }
              }
            }
            """);

    [When("the UI begins accepting a URL elicitation")]
    public async Task WhenTheUiBeginsAcceptingAUrlElicitation()
    {
        myUi.BlockUrlCompletion = true;
        myPendingReceive = ReceiveAsync(
            """
            {
              "version": 3,
              "type": "elicitation.respond",
              "role": "writer",
              "requestId": "url-1",
              "payload": { "action": "accept" }
            }
            """);
        await myUi.UrlCompletionStarted.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Then("the URL is not opened before response completion")]
    public void ThenTheUrlIsNotOpenedBeforeResponseCompletion()
    {
        Assert.Multiple(() =>
        {
            Assert.That(myPendingReceive!.IsCompleted, Is.False);
            Assert.That(
                myUi.Calls,
                Is.EqualTo(new[]
                {
                    "elicitation.lookup:writer:url-1",
                    "elicitation.complete.started:writer:url-1:accept:null",
                }));
        });
    }

    [When("URL elicitation completion finishes")]
    public async Task WhenUrlElicitationCompletionFinishes()
    {
        myUi.ReleaseUrlCompletion();
        await myPendingReceive!;
    }

    [Then("the recording UI received these calls in order:")]
    public void ThenTheRecordingUiReceivedTheseCallsInOrder(Table table) =>
        Assert.That(
            myUi.Calls,
            Is.EqualTo(table.Rows.Select(row => row["call"])));

    [Then("no protocol message is serialized")]
    public void ThenNoProtocolMessageIsSerialized() =>
        Assert.That(mySerializedMessages, Is.Empty);

    [When("the invalid {string} UI message is received")]
    public async Task WhenTheInvalidUiMessageIsReceived(string messageCase)
    {
        ResetRecordings();
        var (message, expectedError) = InvalidMessage(messageCase);
        myExpectedProtocolError = expectedError;
        await ReceiveAsync(message);
    }

    [Then("its exact protocol error is serialized")]
    public void ThenItsExactProtocolErrorIsSerialized() =>
        AssertProtocolError(myExpectedProtocolError!);

    [Then("no UI or transcript command was invoked")]
    public void ThenNoUiOrTranscriptCommandWasInvoked() =>
        Assert.That(myUi.Calls, Is.Empty);

    [Given("prompt commands fail with {string}")]
    public void GivenPromptCommandsFailWith(string message) =>
        myUi.PromptFailure = message;

    [Given("the next protocol send fails with {string}")]
    public void GivenTheNextProtocolSendFailsWith(string message)
    {
        mySerializedMessageFailure = message;
        mySerializedMessageFailuresRemaining = 1;
    }

    [Then("protocol error {string} is serialized")]
    public void ThenProtocolErrorIsSerialized(string message) =>
        AssertProtocolError(message);

    [Then("protocol errors {string} and {string} were attempted")]
    public void ThenProtocolErrorsWereAttempted(
        string firstMessage,
        string secondMessage)
    {
        var envelopes = mySerializedMessageAttempts
            .Select(message =>
                JsonSerializer.Deserialize<JsonElement>(message))
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(
                envelopes.Select(envelope =>
                    envelope.GetProperty("type").GetString()),
                Is.EqualTo(new[]
                {
                    "protocol.error",
                    "protocol.error",
                }));
            Assert.That(
                envelopes.Select(envelope =>
                    envelope.GetProperty("payload")
                        .GetProperty("message").GetString()),
                Is.EqualTo(new[] { firstMessage, secondMessage }));
        });
    }

    [When("the UI responds to an unknown elicitation")]
    public Task WhenTheUiRespondsToAnUnknownElicitation() =>
        ReceiveAsync(
            """
            {
              "version": 3,
              "type": "elicitation.respond",
              "role": "writer",
              "requestId": "missing",
              "payload": { "action": "cancel" }
            }
            """);

    public async ValueTask DisposeAsync()
    {
        myUi.ReleaseUrlCompletion();
        if (myHost is not null)
        {
            var disposal = myHost.DisposeAsync().AsTask();
            myPublisherContext.Drain();
            await disposal;
        }
    }

    private PhotinoWindowHost Host =>
        myHost
        ?? throw new InvalidOperationException(
            "The recording Photino host was not configured.");

    private Task ReceiveAsync(string message) =>
        Host.ReceiveMessageAsync(message);

    private void ResetRecordings()
    {
        myUi.ClearCalls();
        mySerializedMessages.Clear();
        mySerializedMessageAttempts.Clear();
    }

    private void RecordSerializedMessage(string message)
    {
        mySerializedMessageAttempts.Enqueue(message);
        if (Interlocked.Exchange(
                ref mySerializedMessageFailuresRemaining,
                0) == 1)
            throw new InvalidOperationException(mySerializedMessageFailure);
        mySerializedMessages.Enqueue(message);
    }

    private void AssertCalls(params string[] calls) =>
        Assert.That(myUi.Calls, Is.EqualTo(calls));

    private void AssertSerializedTypes(params string[] types) =>
        Assert.That(
            mySerializedMessages
                .Select(message =>
                    JsonSerializer.Deserialize<JsonElement>(message)
                        .GetProperty("type").GetString()),
            Is.EqualTo(types));

    private void AssertProtocolError(string message)
    {
        var envelopes = SerializedMessages();
        Assert.That(envelopes, Has.Count.EqualTo(1));
        var envelope = envelopes.Single();
        Assert.Multiple(() =>
        {
            Assert.That(
                envelope.GetProperty("type").GetString(),
                Is.EqualTo("protocol.error"));
            Assert.That(
                envelope.GetProperty("payload")
                    .GetProperty("message").GetString(),
                Is.EqualTo(message));
        });
    }

    private JsonElement LastSerializedMessage(string type) =>
        SerializedMessages(type).Last();

    private IReadOnlyList<JsonElement> SerializedMessages(string type) =>
        SerializedMessages()
            .Where(message =>
                message.GetProperty("type").GetString() == type)
            .ToArray();

    private IReadOnlyList<JsonElement> SerializedMessages() =>
        mySerializedMessages
            .Select(message =>
                JsonSerializer.Deserialize<JsonElement>(message))
            .ToArray();

    private static (string Message, string ExpectedError) InvalidMessage(
        string messageCase) =>
        messageCase switch
        {
            "unsupported version" => (
                """{"version":2,"type":"role.abort","role":"coder"}""",
                "The UI protocol version is not supported."),
            "missing type" => (
                """{"version":3}""",
                "The UI message is missing a type."),
            "unknown type" => (
                """{"version":3,"type":"unknown"}""",
                "Unknown UI message type 'unknown'."),
            "missing role" => (
                """
                {
                  "version": 3,
                  "type": "prompt.send",
                  "payload": { "prompt": "hello" }
                }
                """,
                "The UI message is missing role."),
            "missing request ID" => (
                """
                {
                  "version": 3,
                  "type": "permission.respond",
                  "role": "coder",
                  "payload": { "approved": true }
                }
                """,
                "The UI message is missing requestId."),
            "invalid string payload" => (
                """
                {
                  "version": 3,
                  "type": "prompt.send",
                  "role": "coder",
                  "payload": { "prompt": 42 }
                }
                """,
                "The UI message is missing payload.prompt."),
            "invalid boolean payload" => (
                """
                {
                  "version": 3,
                  "type": "permission.respond",
                  "role": "coder",
                  "requestId": "permission-1",
                  "payload": { "approved": "yes" }
                }
                """,
                "The UI message is missing payload.approved."),
            "invalid integer payload" => (
                """
                {
                  "version": 3,
                  "type": "transcript.page",
                  "role": "coder",
                  "payload": { "beforeIndex": "five" }
                }
                """,
                "The requested operation requires an element of type "
                + "'Number', but the target element has type 'String'."),
            "invalid synchronization payload" => (
                """
                {
                  "version": 3,
                  "type": "transcript.synchronize",
                  "payload": { "roles": "coder" }
                }
                """,
                "The UI message contains invalid transcript positions."),
            "malformed JSON" => (
                "{",
                "Expected depth to be zero at the end of the JSON payload. "
                + "There is an open JSON object or array that should be closed. "
                + "LineNumber: 0 | BytePositionInLine: 1."),
            _ => throw new ArgumentOutOfRangeException(
                nameof(messageCase),
                messageCase,
                "Unknown invalid message case."),
        };
}




