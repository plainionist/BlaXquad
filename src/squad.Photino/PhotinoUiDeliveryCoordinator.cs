using global::squad.Ui.Abstractions;

namespace squad.Photino;

internal sealed class PhotinoUiDeliveryCoordinator : IAsyncDisposable
{
    private const int myMaxTranscriptSynchronizationEntriesPerRole = 500;
    private const int myMaxPendingTranscriptUpdates = 1024;
    private const int myMaxRecoveryAnnouncementCharactersPerRole = 65_536;
    private const int myMaxRecoveryAnnouncementUpdatesPerRole = 2048;
    private static readonly TimeSpan mySnapshotInterval =
        TimeSpan.FromMilliseconds(33);
    private readonly ISquadUi myUi;
    private readonly ITranscriptUi myTranscriptUi;
    private readonly Action<string, object> mySend;
    private readonly object myTranscriptUpdatesLock = new();
    private readonly SnapshotPublisher mySnapshotPublisher;
    private readonly List<TranscriptUpdate> myTranscriptUpdates = [];
    private readonly TranscriptAnnouncementJournal
        myTranscriptAnnouncementJournal = new(
            myMaxRecoveryAnnouncementUpdatesPerRole,
            myMaxRecoveryAnnouncementCharactersPerRole);
    private readonly Dictionary<string, long> myDeliveredTranscriptSequences =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> mySynchronizedTranscriptSequences =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, TranscriptSynchronizationPosition>
        myRequestedTranscriptPositions =
        new(StringComparer.Ordinal);
    private bool myTranscriptUpdatesRequireSynchronization;
    private bool myTranscriptSynchronizationRequested;
    private bool myInitialTranscriptSynchronizationRequested;

    internal PhotinoUiDeliveryCoordinator(
        ISquadUi ui,
        ITranscriptUi transcriptUi,
        Action<string, object> send)
    {
        myUi = ui;
        myTranscriptUi = transcriptUi;
        mySend = send;
        mySnapshotPublisher = new SnapshotPublisher(
            () =>
            {
                PublishSnapshot();
                return Task.CompletedTask;
            },
            mySnapshotInterval);
    }

    internal void RequestStateRefresh(UiRefreshPriority priority) =>
        mySnapshotPublisher.Request(priority);

    internal void QueueTranscriptUpdate(TranscriptUpdate update)
    {
        lock (myTranscriptUpdatesLock)
        {
            myTranscriptAnnouncementJournal.Add(update);
            if (!myTranscriptUpdatesRequireSynchronization
                && myTranscriptUpdates.Count == myMaxPendingTranscriptUpdates)
            {
                myTranscriptUpdates.Clear();
                myTranscriptUpdatesRequireSynchronization = true;
            }
            else if (!myTranscriptUpdatesRequireSynchronization)
            {
                myTranscriptUpdates.Add(update);
            }
        }
        mySnapshotPublisher.Request(UiRefreshPriority.Deferred);
    }

    internal void RequestTranscriptSynchronization(
        bool initial = false,
        IReadOnlyDictionary<string, TranscriptSynchronizationPosition>?
            positions = null)
    {
        lock (myTranscriptUpdatesLock)
        {
            myTranscriptSynchronizationRequested = true;
            myInitialTranscriptSynchronizationRequested |= initial;
            if (positions is not null)
            {
                foreach (var (role, position) in positions)
                {
                    if (!myRequestedTranscriptPositions.TryGetValue(
                            role,
                            out var existing))
                    {
                        myRequestedTranscriptPositions[role] = position;
                        continue;
                    }
                    myRequestedTranscriptPositions[role] = new(
                        Math.Min(
                            existing.VisualSequence,
                            position.VisualSequence),
                        Math.Min(
                            existing.AnnouncementSequence,
                            position.AnnouncementSequence));
                }
            }
        }
        mySnapshotPublisher.Request(UiRefreshPriority.Immediate);
    }

    internal Task SessionsStartedAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        mySnapshotPublisher.Request(UiRefreshPriority.Immediate);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() =>
        mySnapshotPublisher.DisposeAsync();

    private void PublishSnapshot()
    {
        List<TranscriptUpdate> updates;
        bool updatesRequireSynchronization;
        bool synchronizationRequested;
        bool initialSynchronizationRequested;
        Dictionary<string, TranscriptSynchronizationPosition>
            recoveryBaselines;
        Dictionary<string, long> lastSynchronizedSequences;
        lock (myTranscriptUpdatesLock)
        {
            updates = [.. myTranscriptUpdates];
            myTranscriptUpdates.Clear();
            updatesRequireSynchronization =
                myTranscriptUpdatesRequireSynchronization;
            myTranscriptUpdatesRequireSynchronization = false;
            synchronizationRequested = myTranscriptSynchronizationRequested;
            myTranscriptSynchronizationRequested = false;
            initialSynchronizationRequested =
                myInitialTranscriptSynchronizationRequested;
            myInitialTranscriptSynchronizationRequested = false;
            recoveryBaselines = new(
                myRequestedTranscriptPositions,
                StringComparer.Ordinal);
            myRequestedTranscriptPositions.Clear();
            lastSynchronizedSequences = new(
                mySynchronizedTranscriptSequences,
                StringComparer.Ordinal);
            if (updatesRequireSynchronization)
            {
                foreach (var (role, sequence)
                         in myDeliveredTranscriptSequences)
                {
                    if (!recoveryBaselines.TryGetValue(
                            role,
                            out var existing)
                        || sequence < existing.AnnouncementSequence)
                        recoveryBaselines[role] = new(sequence, sequence);
                }
            }
        }

        var synchronize = updatesRequireSynchronization
            || synchronizationRequested;
        var transcriptSnapshot = synchronize
            ? myTranscriptUi.CreateTranscriptSnapshot(
                myMaxTranscriptSynchronizationEntriesPerRole)
            : null;
        var recovery = synchronize && !initialSynchronizationRequested;
        var recoveryAnnouncements =
            synchronize && transcriptSnapshot is not null
                ? transcriptSnapshot.ToDictionary(
                    role => role.Role,
                    role => myTranscriptAnnouncementJournal.Read(
                        role.Role,
                        recoveryBaselines.TryGetValue(
                            role.Role,
                            out var position)
                            ? position.AnnouncementSequence
                            : lastSynchronizedSequences.GetValueOrDefault(
                                role.Role,
                                role.Sequence),
                        role.Sequence),
                    StringComparer.Ordinal)
                : null;

        mySend("state.snapshot", myUi.CreateSnapshot());
        if (transcriptSnapshot is not null)
        {
            mySend(
                "transcript.synchronize",
                PhotinoTranscriptProtocol.CreateSynchronizationPayload(
                    transcriptSnapshot,
                    recoveryAnnouncements,
                    recovery));
            lock (myTranscriptUpdatesLock)
            {
                foreach (var role in transcriptSnapshot)
                {
                    myDeliveredTranscriptSequences[role.Role] = role.Sequence;
                    mySynchronizedTranscriptSequences[role.Role] =
                        role.Sequence;
                }
            }
        }

        var synchronizedSequences = transcriptSnapshot?.ToDictionary(
            role => role.Role,
            role => role.Sequence,
            StringComparer.Ordinal);
        foreach (var update in updates)
        {
            if (synchronizedSequences?.TryGetValue(
                    update.Role,
                    out var sequence) == true
                && update.Sequence <= sequence)
                continue;
            mySend(
                "transcript.update",
                PhotinoTranscriptProtocol.CreateUpdatePayload(update));
            lock (myTranscriptUpdatesLock)
                myDeliveredTranscriptSequences[update.Role] = update.Sequence;
        }
    }
}




