using System.Text.Json;
using squad.Ui.Abstractions;

namespace squad.Ui.Protocol;

/// <summary>
/// Owns versioned UI message framing, command routing, event subscriptions, snapshot scheduling, and transcript
/// recovery independently of the native window.
/// </summary>
public sealed class UiProtocolSession : IAsyncDisposable
{
    private const int myProtocolVersion = 3;
    private readonly ISquadUi myUi;
    private readonly ITranscriptUi myTranscriptUi;
    private readonly Action<string> mySendSerializedMessage;
    private readonly UiCommandHandler myCommandHandler;
    private readonly UiDeliveryCoordinator myDeliveryCoordinator;

    public UiProtocolSession(
        ISquadUi ui,
        Action<string> sendSerializedMessage,
        Action signalUiReady)
    {
        myUi = ui;
        myTranscriptUi = ui as ITranscriptUi
            ?? throw new ArgumentException("The Photino UI must support incremental transcripts.", nameof(ui));
        mySendSerializedMessage = sendSerializedMessage;
        myDeliveryCoordinator = new(myUi, myTranscriptUi, Send);
        myCommandHandler = new(
            myUi,
            myTranscriptUi,
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
        try
        {
            var message = UiMessageReader.Read(
                serializedMessage,
                myProtocolVersion);
            if (message.EnvelopeError is not null)
            {
                PublishError(message.EnvelopeError);
                return;
            }
            await myCommandHandler.HandleAsync(message);
        }
        catch (Exception exception)
        {
            PublishError(exception.Message);
        }
    }

    public ValueTask DisposeAsync() => myDeliveryCoordinator.DisposeAsync();

    private void PublishError(string message) => Send("protocol.error", new { message });

    private void Send(string type, object payload) =>
        mySendSerializedMessage(
            JsonSerializer.Serialize(
                new { version = myProtocolVersion, type, payload }));
}
