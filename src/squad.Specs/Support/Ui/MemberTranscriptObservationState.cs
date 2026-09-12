namespace squad.Specs.Support.Ui;

/// <summary>Test-owned, per-member bookkeeping accumulated while a scenario observes one role's transcript
/// synchronization, paging, and delta activity - replacing what used to be six separate role-keyed dictionaries in
/// <see cref="squad.Specs.StepDefinitions.BackendScenarioSteps"/> with one registry entry per member. The nullable
/// members preserve the distinction between "never observed" and a genuine zero/default value.</summary>
public sealed class MemberTranscriptObservationState
{
    /// <summary>The entry index to page back from next, seeded from the oldest entry of the latest transcript
    /// synchronization or page observed for this role. Null until either has been observed.</summary>
    public int? PageFrontier { get; set; }

    /// <summary>How many "previous page" responses have been observed for this role, used to skip already-seen
    /// pages when waiting for the next one.</summary>
    public int PagesObserved { get; set; }

    /// <summary>The most recently observed "previous page" response for this role.</summary>
    public TranscriptPageObservation? LatestPage { get; set; }

    /// <summary>Every entry observed so far for this role, accumulated across the live synchronization and every
    /// "previous page" response fetched afterward.</summary>
    public List<TranscriptEntryObservation> PagedEntries { get; } = [];

    /// <summary>How many "assistant" deltas with an explicit character count have been emitted for this role.</summary>
    public int AssistantDeltaCount { get; set; }

    /// <summary>The sequence reported by the most recent transcript synchronization observed for this role. Null
    /// until one has been observed.</summary>
    public long? LatestSynchronizedSequence { get; set; }
}
