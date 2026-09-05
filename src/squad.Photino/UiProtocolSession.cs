using System.Text.Json;
using global::squad.Ui.Abstractions;

namespace squad.Photino;

/// <summary>
/// Owns the versioned Photino UI protocol independent of the native window:
/// envelope parsing and serialization, protocol-error publication, command
/// routing, UI event subscriptions, snapshot scheduling, transcript
/// sequencing, and recovery coordination.
/// </summary>
public sealed class UiProtocolSession : IAsyncDisposable
{
    private const int myProtocolVersion = 3;
    private readonly ISquadUi myUi;
    private readonly ITranscriptUi myTranscriptUi;
    private readonly Action<string> mySendSerializedMessage;
    private readonly PhotinoUiCommandHandler myCommandHandler;
    private readonly PhotinoUiDeliveryCoordinator myDeliveryCoordinator;

    public UiProtocolSession(
        ISquadUi ui,
        Action<string> sendSerializedMessage,
        Action signalUiReady,
        Action requestSmokeShutdown,
        Action<string>? openExternalUrl = null)
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
            signalUiReady,
            requestSmokeShutdown,
            openExternalUrl);
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

    public async Task ReceiveMessageAsync(string serializedMessage)
    {
        try
        {
            var message = PhotinoUiMessageReader.Read(
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
