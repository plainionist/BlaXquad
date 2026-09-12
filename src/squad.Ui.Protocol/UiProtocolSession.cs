using System.Text.Json;
using squad.Ui.Abstractions;

namespace squad.Ui.Protocol;

/// <summary>
/// Owns UI message framing, command routing, event subscriptions, snapshot scheduling, and transcript recovery
/// independently of the native window.
/// </summary>
public sealed class UiProtocolSession : IAsyncDisposable
{
    private readonly ISquadUi myUi;
    private readonly ITranscriptUi myTranscriptUi;
    private readonly Action<string> mySendSerializedMessage;
    private readonly UiCommandHandler myCommandHandler;
    private readonly UiDeliveryCoordinator myDeliveryCoordinator;

    public UiProtocolSession(
        ISquadUi ui,
        IIssueCatalog issueCatalog,
        IWorkspaceTools workspaceTools,
        Action<string> sendSerializedMessage,
        Action signalUiReady)
    {
        myUi = ui;
        Contract.Requires(ui is ITranscriptUi, "The Photino UI must support incremental transcripts.");
        myTranscriptUi = (ITranscriptUi)ui;
        mySendSerializedMessage = sendSerializedMessage;
        myDeliveryCoordinator = new(myUi, myTranscriptUi, (type, payload) => Send(type, payload));
        myCommandHandler = new(
            myUi,
            myTranscriptUi,
            issueCatalog,
            workspaceTools,
            Send,
            myDeliveryCoordinator.RequestTranscriptSynchronization,
            signalUiReady);
    }

    public void AttachUiEventSources()
    {
        myUi.SnapshotRequested += myDeliveryCoordinator.RequestStateRefresh;
        myTranscriptUi.TranscriptChanged += myDeliveryCoordinator.QueueTranscriptUpdate;
    }

    public void DetachUiEventSources()
    {
        myUi.SnapshotRequested -= myDeliveryCoordinator.RequestStateRefresh;
        myTranscriptUi.TranscriptChanged -= myDeliveryCoordinator.QueueTranscriptUpdate;
    }

    public Task SessionsStartedAsync(CancellationToken cancellationToken = default) =>
        myDeliveryCoordinator.SessionsStartedAsync(cancellationToken);

    /// <summary>
    /// Parses and dispatches one serialized message. Invalid envelopes, payloads, and command failures are returned
    /// to the UI as protocol errors rather than escaping to the native message callback.
    /// </summary>
    public async Task ReceiveMessageAsync(string serializedMessage)
    {
        UiMessage message;
        try
        {
            message = UiMessageReader.Read(serializedMessage);
        }
        catch (Exception exception)
        {
            PublishError(exception.Message, null);
            return;
        }
        if (message.EnvelopeError is not null)
        {
            PublishError(message.EnvelopeError, null);
            return;
        }
        try
        {
            await myCommandHandler.HandleAsync(message);
        }
        catch (Exception exception)
        {
            // A correlated request (one carrying a requestId, such as "issues.list") echoes that ID on failure so
            // the UI can resolve its own loading state without treating an unrelated protocol failure as its own.
            PublishError(exception.Message, message.RequestId);
        }
    }

    public ValueTask DisposeAsync() => myDeliveryCoordinator.DisposeAsync();

    private void PublishError(string message, string? requestId) =>
        Send("protocol.error", new { message }, requestId);

    private void Send(string type, object payload, string? requestId = null)
    {
        object envelope = requestId is null
            ? new { type, payload }
            : new { type, payload, requestId };
        mySendSerializedMessage(JsonSerializer.Serialize(envelope));
    }
}
