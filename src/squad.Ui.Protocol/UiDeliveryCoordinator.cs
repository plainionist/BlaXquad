using squad.Domain;
using squad.Ui.Abstractions;

namespace squad.Ui.Protocol;

/// <summary>
/// Orders state snapshots, incremental transcript updates, and transcript recovery for one UI connection. Pending
/// updates that exceed the bounded queue are replaced by a synchronization snapshot.
/// </summary>
internal sealed class UiDeliveryCoordinator : IAsyncDisposable
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
    private readonly Dictionary<SquadMemberId, long> myDeliveredTranscriptSequences = [];
    private readonly Dictionary<SquadMemberId, long> mySynchronizedTranscriptSequences = [];
    private readonly Dictionary<SquadMemberId, TranscriptSynchronizationPosition>
        myRequestedTranscriptPositions = [];
    private bool myTranscriptUpdatesRequireSynchronization;
    private bool myTranscriptSynchronizationRequested;
    private bool myInitialTranscriptSynchronizationRequested;

    internal UiDeliveryCoordinator(
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
        IReadOnlyDictionary<SquadMemberId, TranscriptSynchronizationPosition>?
            positions = null)
    {
        lock (myTranscriptUpdatesLock)
        {
            myTranscriptSynchronizationRequested = true;
            myInitialTranscriptSynchronizationRequested |= initial;
            if (positions is not null)
            {
                foreach (var (memberId, position) in positions)
                {
                    if (!myRequestedTranscriptPositions.TryGetValue(
                            memberId,
                            out var existing))
                    {
                        myRequestedTranscriptPositions[memberId] = position;
                        continue;
                    }
                    myRequestedTranscriptPositions[memberId] = new(
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
        Dictionary<SquadMemberId, TranscriptSynchronizationPosition>
            recoveryBaselines;
        Dictionary<SquadMemberId, long> lastSynchronizedSequences;
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
            recoveryBaselines = new(myRequestedTranscriptPositions);
            myRequestedTranscriptPositions.Clear();
            lastSynchronizedSequences = new(mySynchronizedTranscriptSequences);
            if (updatesRequireSynchronization)
            {
                foreach (var (memberId, sequence)
                         in myDeliveredTranscriptSequences)
                {
                    if (!recoveryBaselines.TryGetValue(
                            memberId,
                            out var existing)
                        || sequence < existing.AnnouncementSequence)
                    {
                        recoveryBaselines[memberId] = new(sequence, sequence);
                    }
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
                    role => role.MemberId,
                    role => myTranscriptAnnouncementJournal.Read(
                        role.MemberId,
                        recoveryBaselines.TryGetValue(
                            role.MemberId,
                            out var position)
                            ? position.AnnouncementSequence
                            : lastSynchronizedSequences.GetValueOrDefault(
                                role.MemberId,
                                role.Sequence),
                        role.Sequence))
                : null;

        mySend("state.snapshot", myUi.CreateSnapshot());
        if (transcriptSnapshot is not null)
        {
            mySend(
                "transcript.synchronize",
                TranscriptProtocol.CreateSynchronizationPayload(
                    transcriptSnapshot,
                    recoveryAnnouncements,
                    recovery));
            lock (myTranscriptUpdatesLock)
            {
                foreach (var role in transcriptSnapshot)
                {
                    Contract.Invariant(
                        role.Sequence >= myDeliveredTranscriptSequences.GetValueOrDefault(role.MemberId),
                        "Delivered transcript sequence must never move backward.");
                    Contract.Invariant(
                        role.Sequence >= mySynchronizedTranscriptSequences.GetValueOrDefault(role.MemberId),
                        "Synchronized transcript sequence must never move backward.");
                    myDeliveredTranscriptSequences[role.MemberId] = role.Sequence;
                    mySynchronizedTranscriptSequences[role.MemberId] =
                        role.Sequence;
                }
            }
        }

        var synchronizedSequences = transcriptSnapshot?.ToDictionary(
            role => role.MemberId,
            role => role.Sequence);
        foreach (var update in updates)
        {
            if (synchronizedSequences?.TryGetValue(
                    update.MemberId,
                    out var sequence) == true
                && update.Sequence <= sequence)
            {
                continue;
            }
            mySend(
                "transcript.update",
                TranscriptProtocol.CreateUpdatePayload(update));
            lock (myTranscriptUpdatesLock)
            {
                Contract.Invariant(
                    update.Sequence >= myDeliveredTranscriptSequences.GetValueOrDefault(update.MemberId),
                    "Delivered transcript sequence must never move backward.");
                myDeliveredTranscriptSequences[update.MemberId] = update.Sequence;
            }
        }
    }
}


