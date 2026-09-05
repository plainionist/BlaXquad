namespace squad.Specs.Support;

/// <summary>
/// Test-only ordering recorder shared by the recording window, backend, sessions, handoff pump, and process-wide
/// resources so black-box scenarios can characterize the observable ownership order of <c>SquadApplication</c>'s
/// generation lifecycle without adding any production instrumentation.
/// </summary>
public sealed class LifecycleTrace
{
    private readonly object myLock = new();
    private readonly List<string> myMilestones = [];

    public void Record(string milestone)
    {
        lock (myLock)
            myMilestones.Add(milestone);
    }

    public IReadOnlyList<string> Milestones
    {
        get
        {
            lock (myLock)
                return [.. myMilestones];
        }
    }

    public void AssertOrdered(string earlier, string later)
    {
        var milestones = Milestones.ToList();
        var earlierIndex = milestones.IndexOf(earlier);
        var laterIndex = milestones.IndexOf(later);
        Assert.That(earlierIndex, Is.GreaterThanOrEqualTo(0), $"Expected '{earlier}' to be recorded. Trace was: {string.Join(" -> ", milestones)}");
        Assert.That(laterIndex, Is.GreaterThanOrEqualTo(0), $"Expected '{later}' to be recorded. Trace was: {string.Join(" -> ", milestones)}");
        Assert.That(earlierIndex, Is.LessThan(laterIndex), $"Expected '{earlier}' before '{later}'. Trace was: {string.Join(" -> ", milestones)}");
    }
}
