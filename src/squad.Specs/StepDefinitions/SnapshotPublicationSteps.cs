using squad.Ui.Abstractions;
using squad.Photino;

namespace squad.Specs.StepDefinitions;

[Binding]
public sealed class SnapshotPublicationSteps : IAsyncDisposable
{
    private readonly TaskCompletionSource myFirstPublicationStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource myReleaseFirstPublication = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<int> myPublishedStates = [];
    private readonly object myStateLock = new();
    private SnapshotPublisher? myPublisher;
    private Task? myDisposal;
    private int myActivePublications;
    private int myMaximumConcurrency;
    private int myState = 1;

    [Given("a slow snapshot publisher")]
    public void GivenASlowSnapshotPublisher()
    {
        myPublisher = new SnapshotPublisher(PublishAsync, TimeSpan.FromMilliseconds(20));
    }

    [Given("a snapshot publisher with a long deferred interval")]
    public void GivenASnapshotPublisherWithALongDeferredInterval()
    {
        myPublisher = new SnapshotPublisher(PublishAsync, TimeSpan.FromSeconds(5));
        myReleaseFirstPublication.TrySetResult();
    }

    [When("an immediate snapshot starts")]
    public async Task WhenAnImmediateSnapshotStarts()
    {
        Publisher.Request(UiRefreshPriority.Immediate);
        await myFirstPublicationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [When("deferred and immediate refreshes arrive during publication")]
    public void WhenDeferredAndImmediateRefreshesArriveDuringPublication()
    {
        Volatile.Write(ref myState, 2);
        for (var index = 0; index < 100; index++)
            Publisher.Request(UiRefreshPriority.Deferred);
        Publisher.Request(UiRefreshPriority.Immediate);
    }

    [When("a deferred snapshot is requested")]
    public void WhenADeferredSnapshotIsRequested() =>
        Publisher.Request(UiRefreshPriority.Deferred);

    [When("an immediate snapshot is requested")]
    public void WhenAnImmediateSnapshotIsRequested() =>
        Publisher.Request(UiRefreshPriority.Immediate);

    [When("a follow-up refresh is queued")]
    public void WhenAFollowUpRefreshIsQueued() =>
        Publisher.Request(UiRefreshPriority.Immediate);

    [When("snapshot publisher disposal starts")]
    public void WhenSnapshotPublisherDisposalStarts()
    {
        myDisposal = Publisher.DisposeAsync().AsTask();
    }

    [When("the first snapshot is allowed to finish")]
    public async Task WhenTheFirstSnapshotIsAllowedToFinish()
    {
        myReleaseFirstPublication.TrySetResult();
        await WaitUntilAsync(() =>
        {
            lock (myStateLock)
                return myPublishedStates.Count == 2;
        });
    }

    [When("the active snapshot is allowed to finish")]
    public void WhenTheActiveSnapshotIsAllowedToFinish() =>
        myReleaseFirstPublication.TrySetResult();

    [Then("snapshot publication concurrency never exceeds one")]
    public void ThenSnapshotPublicationConcurrencyNeverExceedsOne() =>
        Assert.That(Volatile.Read(ref myMaximumConcurrency), Is.EqualTo(1));

    [Then("exactly one follow-up snapshot contains the latest state")]
    public void ThenExactlyOneFollowUpSnapshotContainsTheLatestState()
    {
        lock (myStateLock)
            Assert.That(myPublishedStates, Is.EqualTo(new[] { 1, 2 }));
    }

    [Then("disposing the snapshot publisher stops further publication")]
    public async Task ThenDisposingTheSnapshotPublisherStopsFurtherPublication()
    {
        await Publisher.DisposeAsync();
        Publisher.Request(UiRefreshPriority.Immediate);
        await Task.Delay(50);
        lock (myStateLock)
            Assert.That(myPublishedStates, Has.Count.EqualTo(2));
    }

    [Then("no snapshot is published during the deferred interval")]
    public async Task ThenNoSnapshotIsPublishedDuringTheDeferredInterval()
    {
        await Task.Delay(100);
        lock (myStateLock)
            Assert.That(myPublishedStates, Is.Empty);
    }

    [Then("one snapshot is published without the deferred delay")]
    public async Task ThenOneSnapshotIsPublishedWithoutTheDeferredDelay()
    {
        await WaitUntilAsync(() =>
        {
            lock (myStateLock)
                return myPublishedStates.Count == 1;
        });
    }

    [Then("snapshot publisher disposal is waiting")]
    public void ThenSnapshotPublisherDisposalIsWaiting() =>
        Assert.That(Disposal.IsCompleted, Is.False);

    [Then("snapshot publisher disposal completes")]
    public async Task ThenSnapshotPublisherDisposalCompletes() =>
        await Disposal.WaitAsync(TimeSpan.FromSeconds(5));

    [Then("no follow-up snapshot is published")]
    public async Task ThenNoFollowUpSnapshotIsPublished()
    {
        await Task.Delay(50);
        lock (myStateLock)
            Assert.That(myPublishedStates, Has.Count.EqualTo(1));
    }

    public async ValueTask DisposeAsync()
    {
        myReleaseFirstPublication.TrySetResult();
        if (myPublisher is not null)
            await myPublisher.DisposeAsync();
    }

    private SnapshotPublisher Publisher =>
        myPublisher ?? throw new InvalidOperationException("The snapshot publisher was not configured.");

    private Task Disposal =>
        myDisposal ?? throw new InvalidOperationException("Snapshot publisher disposal was not started.");

    private async Task PublishAsync()
    {
        var active = Interlocked.Increment(ref myActivePublications);
        UpdateMaximumConcurrency(active);
        var state = Volatile.Read(ref myState);
        if (state == 1)
        {
            myFirstPublicationStarted.TrySetResult();
            await myReleaseFirstPublication.Task;
        }
        lock (myStateLock)
            myPublishedStates.Add(state);
        Interlocked.Decrement(ref myActivePublications);
    }

    private void UpdateMaximumConcurrency(int active)
    {
        while (true)
        {
            var current = Volatile.Read(ref myMaximumConcurrency);
            if (current >= active)
                return;
            if (Interlocked.CompareExchange(ref myMaximumConcurrency, active, current) == current)
                return;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 500; attempt++)
        {
            if (condition())
                return;
            await Task.Delay(10);
        }
        Assert.Fail("Timed out waiting for snapshot publication.");
    }
}



