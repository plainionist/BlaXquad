---
title: multiple dictionaries
priority: 10
---

identify types with multiple members of dictionaries with same key.
example: src\squad.AgentProvider.Fake\Control\ObservationJournal.cs
example: src\squad.Application\Members\MemberAggregate.cs

this is typically an indicator that we should rather have another type 
encapsulating the various aspects and then only have one member with collection
or dictionary of the new type

analyze the code base - identify such cases and suggest design improvements 
to streamline ownership

## Analysis

The smell is not the number of dictionaries by itself. It occurs when the same key denotes the same domain identity
and the values are different aspects or lifecycle phases of that identity. In that case, the containing type is
coordinating parallel indexes instead of delegating the state transition to one per-key object. A useful test is:

> If removing or completing one key requires knowing which other collections contain that key, the values belong
> to one aggregate stored in one dictionary.

The audit covered non-generated C# under `src` and excluded local dictionaries, parameters, build output, and types
with only one dictionary field. It also looked for semantic companions such as sets whose keys represent another
boolean aspect of a dictionary entry.

### Findings

| Priority | Owner | Parallel state | Assessment |
| --- | --- | --- | --- |
| High | [`MemberAggregate`](../../src/squad.Application/Members/MemberAggregate.cs) | Four maps keyed by `InteractionRequestId`: permission, input, elicitation, and protected transcript entry | One pending member interaction is split by request kind and transcript-retention state. Consolidate. |
| High | [`CopilotSdkAgentSession`](../../src/squad.AgentProvider.CopilotSdk/CopilotSdkAgentSession.cs) | Three maps keyed by `InteractionRequestId`, each holding a typed response completion | One provider interaction registry is split by response type. Consolidate. |
| High | [`UiDeliveryCoordinator`](../../src/squad.Ui.Protocol/UiDeliveryCoordinator.cs) | Delivered sequence, synchronized sequence, and requested recovery position keyed by `SquadMemberId` | These are phases of one member's transcript-delivery cursor. Consolidate. |
| Medium | [`ObservationJournal`](../../src/squad.AgentProvider.Fake/Control/ObservationJournal.cs) | Active session and latest prompt keyed by `SquadMemberId`; latest value and count keyed by `(SquadMemberId, Kind)` | Both groups are per-member observation state. Consolidate into a member journal with nested observations by kind. |
| Medium | [`TranscriptArchive`](../../src/squad.Application/Transcripts/TranscriptArchive.cs) | Retained lengths by member/index, total lengths by `(member, index)`, truncated-entry keys, and truncated-member keys | Not a literal same-key-only match, but it is the same smell: one archived entry and role are represented across four indexes. Consolidate. |
| Medium | [`RawSdkEventTrace`](../../src/squad.AgentProvider.CopilotSdk/RawSdkEventTrace.cs) | Previous payload and tool name keyed by SDK tool-call id, currently represented as `string` | Both values have the same tool-call lifecycle and are removed together on completion. Consolidate. |
| Low | [`BackendScenarioSteps`](../../src/squad.Specs/StepDefinitions/BackendScenarioSteps.cs) | Six transcript-observation maps keyed by `SquadMemberId` | Test-only, but still one per-member transcript observation state. Consolidation would make paging state transitions easier to read. |
| Low | [`HostCoexistenceSteps`](../../src/squad.Specs/StepDefinitions/HostCoexistenceSteps.cs) | Scenario and observed exit code keyed by project label | Test-only project state with one lifecycle. Consolidate when touching this fixture. |

### 1. Member interaction state

`MemberAggregate` is the clearest production case. `MemberEventProjector` first registers a request and then records
the transcript entry protected by that request. Response handling removes the typed request, invokes the provider,
restores the request if delivery fails, and later removes the protection. Terminal cleanup clears every request map
and walks the protection map to unprotect transcript entries.

Replace the four maps with:

```text
Dictionary<InteractionRequestId, MemberInteractionState>

MemberInteractionState
	Pending: exactly one request kind + protected transcript entry index
	Responding: exactly one request kind + protected transcript entry index
	RetainedForRetirement: protected transcript entry index
```

Prefer an explicit closed hierarchy or discriminated representation over nullable request properties and freely
combinable flags. The type must make "exactly one request kind" valid by construction for pending and responding
states, while the retirement state deliberately retains no request payload. Expose one owner-level registration
transition that validates request-id uniqueness across kinds, appends and protects the transcript entry, stores the
complete state, and returns the transcript update. Do not preserve the projector's current public sequence of
registering a request, adding a protected entry, and then associating its index.

Response handling should transition `Pending` to `Responding` before calling the provider. Keep that state in the
registry but omit it from pending-interaction snapshots: abort and shutdown cleanup must still be able to find its
protected transcript entry while provider I/O is in flight. A successful response removes the state and unprotects
the entry; a recoverable failure transitions it back to `Pending`; terminal cleanup removes it and unprotects the
entry. This replaces the current coordination between a removed typed request, a retained protection-map entry,
and a request value captured by the asynchronous operation.

Preserve the current distinction between cancellation paths. Abort and session failure remove the interaction and
unprotect its transcript entry. Headquarters shutdown first clears requests from the published pending-interaction
view but deliberately leaves their transcript entries protected for the remainder of the generation's retirement.
It should transition live entries to `RetainedForRetirement`, rather than assuming request visibility and transcript
protection always end together.

This also strengthens an invariant that is currently only implicit: an `InteractionRequestId` can occur at most
once across all request kinds, not merely once within each kind.

Do not share this type with the provider adapter. Application state owns the projected request and transcript
retention; provider state owns response completion. They have the same key but different responsibilities.

### 2. Provider interaction completions

`CopilotSdkAgentSession` repeats registration, completion, cancellation, failure, and cleanup over three typed
dictionaries. A single registry should hold a polymorphic pending completion:

```text
Dictionary<InteractionRequestId, PendingProviderInteraction>

PendingProviderInteraction
	Complete(response): validates the expected response type
	Cancel(token)
	Fail(exception)
	ResponseTask
```

The generic implementation can retain a typed `TaskCompletionSource<TResponse>` while the non-generic base exposes
the lifecycle operations needed by bulk cancellation and failure. Typed response methods must reject a request of
the wrong interaction kind with the existing "no pending interaction" boundary diagnostic, unless a more precise
diagnostic is intentionally specified. One registry makes cancellation and failure one-pass operations and enforces
request-id uniqueness across kinds.

### 3. Transcript delivery cursor

`UiDeliveryCoordinator` should own one `TranscriptDeliveryState` per member:

```text
Dictionary<SquadMemberId, TranscriptDeliveryState>

TranscriptDeliveryState
	DeliveredSequence
	SynchronizedSequence
	RequestedPosition
```

`RequestedPosition` is transient and must be cleared after the publish cycle without discarding the durable delivered
and synchronized sequences. Put the monotonic-sequence invariants on this state object so updates cannot bypass
them. [`TranscriptAnnouncementJournal`](../../src/squad.Ui.Protocol/TranscriptAnnouncementJournal.cs) is the local
model for this design: its dictionary points to a `RoleJournal` that owns correlated queue, count, and sequence
state.

### 4. Fake-provider observation journal

`ObservationJournal` should use one `MemberObservationJournal` per member. That object should own the latest session
id, latest prompt, and a dictionary by observation kind. Each nested `ObservationState` owns both its latest cloned
`JsonElement` and count, which are currently updated together in `RecordObservation`. The ordered lifecycle list and
protocol-error list remain journal-wide because they are event history, not alternate member indexes.

This change also simplifies timeout diagnostics: render each member journal and its nested observations instead of
joining independent maps. Preserve the current behavior that a disposed session remains the latest observed session;
this issue is about ownership, not changing fake-provider semantics.

### 5. Transcript archive index

`TranscriptArchive` should replace its parallel role and entry indexes with a hierarchy:

```text
Dictionary<SquadMemberId, RoleArchiveState>

RoleArchiveState
	Entries: SortedDictionary<int, ArchivedEntryState>
	WasTruncated

ArchivedEntryState
	RetainedContentLength
	TotalContentLength
	IsContentTruncated
```

The sorted dictionary remains necessary for paging and oldest-entry eviction. The improvement is that append,
replace, truncation, read, and eviction mutate one entry object, while the sticky role-level truncation flag belongs
to the role state. This removes composite-key lookups that duplicate the keys already present in the sorted index.

### 6. Tool trace state

`RawSdkEventTrace` should keep one `ToolTraceState` per tool-call id with optional `ToolName` and `PreviousPayload`.
Start and payload events may arrive independently, so both values remain optional; completion removes one entry.
Keep the current payload-classification behavior during this refactor. In particular, changing payload history to be
per payload field would be a separate behavior decision.

### 7. Specification fixture state

`BackendScenarioSteps` can replace its six member-keyed maps with a `MemberTranscriptObservationState` containing
the paging frontier, pages observed, latest page, accumulated entries, assistant delta count, and latest synchronized
sequence. `HostCoexistenceSteps` can similarly use a `ProjectObservationState` containing the required scenario and
optional exit code. These are lower priority because they do not own production state, but they should follow the
same rule when those fixtures next change.

### Explicit non-candidates

- A local dictionary or a method parameter is not ownership state and was not counted.
- Two dictionaries with the same CLR key type are not parallel when the keys name different concepts. No
	consolidation should be based on `string` alone; `RawSdkEventTrace` and `HostCoexistenceSteps` qualify because
	inspection shows that both maps use the same tool-call id or project label respectively.
- A second index is valid when it supports a genuinely different lookup direction and has a documented consistency
	boundary. None of the findings above is such a reverse index; each stores another attribute of the same key.
- Do not introduce a repository-wide generic state-bag abstraction. Each proposed state type belongs beside its
	current owner and should expose transitions in that owner's vocabulary.

## Suggested implementation order

1. Consolidate `MemberAggregate` and preserve interaction publication, failed-response restoration, cancellation,
	 and transcript protection behavior.
2. Consolidate `CopilotSdkAgentSession`; this is the provider-side counterpart but an independent change.
3. Consolidate `UiDeliveryCoordinator`, followed by `TranscriptArchive`.
4. Consolidate `ObservationJournal` and `RawSdkEventTrace`.
5. Clean up the two specification fixtures opportunistically.

Each step should be a behavior-preserving change. Cover production slices through the existing black-box Gherkin
suite, especially interaction publication/cancellation, transcript synchronization/recovery, transcript retention
and paging, and provider failure/abort behavior. Add or adjust scenarios only where the current suite does not prove
the state-preservation invariant being moved; do not add implementation-shaped unit tests for the new state holders.

## Done when

- Each production owner above has one dictionary per domain identity and delegates correlated transitions to its
	per-key state object.
- Request kind, response kind, monotonic transcript sequences, and archive-length/truncation invariants remain
	explicit and valid by construction.
- Existing public protocol payloads, diagnostics, cancellation behavior, and transcript retention behavior remain
	unchanged.
- The relevant black-box Gherkin scenarios pass after every independently reviewable slice.
