---
title: multiple dictionaries
priority: 10
---

identify types with multiple members of dictionaries with same key.
example: src\squad.AgentProvider.Fake\Control\ObservationJournal.cs
example: src\squad.Application\SquadMemberAggregate.cs

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
| High | [`SquadMemberAggregate`](../../src/squad.Application/SquadMemberAggregate.cs) | Four maps keyed by `InteractionRequestId`: permission, input, elicitation, and protected transcript entry | One pending member interaction is split by request kind and transcript-retention state. Consolidate. |
| High | [`CopilotSdkAgentSession`](../../src/squad.AgentProvider.CopilotSdk/CopilotSdkAgentSession.cs) | Three maps keyed by `InteractionRequestId`, each holding a typed response completion | One provider interaction registry is split by response type. Consolidate. |
| High | [`UiDeliveryCoordinator`](../../src/squad.Ui.Protocol/UiDeliveryCoordinator.cs) | Delivered sequence, synchronized sequence, and requested recovery position keyed by `SquadMemberId` | These are phases of one member's transcript-delivery cursor. Consolidate. |
| Medium | [`ObservationJournal`](../../src/squad.AgentProvider.Fake/Control/ObservationJournal.cs) | Active session and latest prompt keyed by `SquadMemberId`; latest value and count keyed by `(SquadMemberId, Kind)` | Both groups are per-member observation state. Consolidate into a member journal with nested observations by kind. |
| Medium | [`TranscriptArchive`](../../src/squad.Application/Transcripts/TranscriptArchive.cs) | Retained lengths by member/index, total lengths by `(member, index)`, truncated-entry keys, and truncated-member keys | Not a literal same-key-only match, but it is the same smell: one archived entry and role are represented across four indexes. Consolidate. |
| Medium | [`RawSdkEventTrace`](../../src/squad.AgentProvider.CopilotSdk/RawSdkEventTrace.cs) | Previous payload and tool name keyed by SDK tool-call id, currently represented as `string` | Both values have the same tool-call lifecycle and are removed together on completion. Consolidate. |
| Low | [`BackendScenarioSteps`](../../src/squad.Specs/StepDefinitions/BackendScenarioSteps.cs) | Six transcript-observation maps keyed by `SquadMemberId` | Test-only, but still one per-member transcript observation state. Consolidation would make paging state transitions easier to read. |
| Low | [`HostCoexistenceSteps`](../../src/squad.Specs/StepDefinitions/HostCoexistenceSteps.cs) | Scenario and observed exit code keyed by project label | Test-only project state with one lifecycle. Consolidate when touching this fixture. |

### 1. Member interaction state

`SquadMemberAggregate` is the clearest production case. `SquadMemberEventProjector` first registers a request and then records
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

### Review findings (cf535d7cd9)

1. **Severity: high.** `src/squad.Application/SquadMemberAggregate.cs` `RestorePending`,
   `src/squad.Application/MemberInteractionState.cs` `Restore`, and
   `src/squad.Application/SquadMemberProcessor.cs` `ExecuteCompleteInteractionAsync` /
   `RunLoopAsync`.
   **Violated behavior:** Headquarters shutdown must transition live interactions to `RetainedForRetirement`,
   leaving them out of the pending view with transcript entries still protected. A recoverable failure of an
   in-flight response must no-op when shutdown (or abort) has already taken that interaction, and must not
   fail the member processor or the shutdown drain.
   **Root cause:** `DrainAsync` calls `ClearInteractions` while a detached response can still be in flight.
   `RestorePending` then finds the retained key and calls `Restore()`, whose default implementation throws
   `InvalidOperationException` for any non-`Responding` variant. `OperationOutcomeMessage` applies that
   mutation on the read loop with no catch, so the throw faults the processor. Abort removes the key (true
   no-op); shutdown deliberately keeps it.
   **Required outcome:** `RestorePending` no-ops unless the state is `Responding`. `RetainedForRetirement`
   stays retained, protected, and unpublished. The read loop must not observe an exception on this path.
   Prove it through the black-box Gherkin suite; add a scenario only if the current suite does not cover
   shutdown overlapping an in-flight failed response.

**Status: resolved (e34423bef1).** `RestorePending` no-ops unless `TryRestore` succeeds on a `Responding`
state. `RetainedForRetirement` stays retained. `InteractionCancellationAndTranscriptRetention.feature` covers
shutdown overlapping an in-flight failed permission response without faulting cleanup.

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

## Implementation plan

Implement the following slices in order. Each numbered slice is one commit and must build and pass its focused
acceptance scenarios before it is handed to review. No slice depends on a later slice to restore behavior or
documentation consistency. Keep each state type beside its owner, in its own source file, and do not introduce a
shared generic state-bag abstraction.

### Slice 1: Consolidate application interaction state

**Task:** `consolidate-member-interaction-state`

**Logical change:** Make `SquadMemberAggregate` the atomic owner of one lifecycle object per interaction request id,
including that interaction's protected transcript entry.

**Implementation:**

- Add a closed `MemberInteractionState` representation under `squad.Application`. Pending and responding variants
  carry exactly one permission, input, or elicitation request plus its protected transcript entry index;
  `RetainedForRetirement` carries only the protected entry index. Do not model request kind with several nullable
  request properties or independently mutable flags.
- Replace `myPermissions`, `myInputs`, `myElicitations`, and `myProtectedTranscriptEntries` with one
  `Dictionary<InteractionRequestId, MemberInteractionState>`. Project pending snapshots by request kind from this
  registry; responding and retirement-only entries are not pending.
- Replace the projector's register/add/protect sequence with one aggregate transition. It must reject an id already
  present under any request kind, append and protect the request's transcript entry, store the complete pending
  state, and return the resulting `TranscriptUpdate`. Keep request-to-transcript presentation mapping in
  `SquadMemberEventProjector`.
- Give response handling explicit transitions: pending to responding before provider I/O; responding to removed on
  success; responding back to the same pending variant after a recoverable provider failure; and responding to
  removed after terminal member failure. Missing ids, wrong request kinds, duplicate responses, and late responses
  retain the existing public diagnostic.
- Abort and session failure remove every live interaction and unprotect every associated entry. Headquarters
  shutdown transitions entries to `RetainedForRetirement` after provider cancellation, removing them from
  snapshots without releasing transcript protection during generation retirement. A late operation outcome must
  not restore a retirement-only interaction.
- Keep identical request ids valid for different members while enforcing uniqueness across interaction kinds within
  one member.

**Acceptance:**

- All scenarios in `InteractionPublicationAndOwnership.feature` and
  `InteractionCancellationAndTranscriptRetention.feature` pass unchanged for publication, routing, response
  validation, cancellation, shutdown, and transcript retention.
- Add one black-box scenario, with the minimum fake-provider support needed, if necessary to make a response fail
  recoverably: the failed response reports its provider error, the same interaction remains pending and protected,
  and a later successful response completes it exactly once.
- No UI protocol payload, request/response contract, or existing diagnostic changes.

**Status: complete (e34423bef1).** `SquadMemberAggregate` owns one `MemberInteractionState` per request id.
Pending/responding variants carry exactly one request kind plus the protected transcript index;
`RetainedForRetirement` carries only the index. Registration is one owner-level transition; response handling
uses pending → responding → pending/removed; shutdown retains protection without republishing. A recoverable
response failure that races shutdown no-ops instead of faulting the processor.

### Slice 2: Consolidate Copilot provider completions

**Task:** `consolidate-copilot-interaction-completions`

**Logical change:** Give `CopilotSdkAgentSession` one provider-side completion registry per interaction id, independent
of the application state introduced by slice 1.

**Implementation:**

- Add a non-generic `PendingProviderInteraction` lifecycle abstraction and a typed implementation that retains its
  `TaskCompletionSource<TResponse>`. The non-generic surface supports cancellation and failure; typed completion
  validates the response kind without casts through `object` or `dynamic`.
- Replace the three typed dictionaries with one
  `Dictionary<InteractionRequestId, PendingProviderInteraction>`. Registration rejects duplicate ids across kinds.
- Complete a response only when both id and response type match, then remove that registration. A wrong response
  kind must leave the real pending completion intact and use the existing "No pending interaction" boundary
  diagnostic.
- Cancel, fault, abort, dispose, and backend/session-failure paths snapshot and clear the single registry once, then
  settle each completion exactly once. The request method's `finally` removes only its own registration so it
  cannot disturb a later entry.

**Acceptance:**

- Permission, input, and elicitation requests still complete with their typed responses; duplicate, late, and
  wrong-kind responses are rejected without completing another request.
- Abort, explicit pending-interaction cancellation, disposal, and provider failure settle every outstanding task
  with the same cancellation token or exception behavior as before.
- The Copilot provider builds and the provider packaging scenarios continue to pass; the provider-neutral
  interaction scenarios remain unchanged.

**Status: complete (03ed4acd7c).** `CopilotSdkAgentSession` owns one `PendingProviderInteraction` registry.
Typed completion matches id and response kind without `object`/`dynamic` casts; a wrong kind leaves the real
pending completion intact and keeps the existing diagnostic. Cancel, fault, abort, dispose, and failure
snapshot-and-clear once; the request `finally` removes only its own registration.

### Slice 3: Consolidate transcript delivery cursors

**Task:** `consolidate-transcript-delivery-state`

**Logical change:** Make one `TranscriptDeliveryState` own each member's durable delivered/synchronized cursors and
transient recovery request.

**Implementation:**

- Replace the three member-keyed dictionaries in `UiDeliveryCoordinator` with one
  `Dictionary<SquadMemberId, TranscriptDeliveryState>`.
- Put monotonic delivered and synchronized sequence transitions on the state object. Synchronization advances both
  cursors and may never precede either; an incremental delivery advances only the delivered cursor.
- Merge repeated requested recovery positions by taking the minimum visual and announcement sequence. Consuming a
  publish cycle clears only that transient request, preserving both durable cursors.
- Derive queue-overflow recovery baselines and last-synchronized snapshots from the same per-member state without
  changing lock scope, message ordering, batching limits, or wire payloads.

**Acceptance:**

- `StdioUiProtocol.feature` and `TranscriptSynchronizationOrder.feature` pass unchanged.
- Initial synchronization, requested recovery, and queue-overflow recovery never regress a sequence, omit a
  post-snapshot update, or emit an update already covered by the synchronization.
- A consumed recovery request does not erase the member's delivered or synchronized cursor.

### Review findings (b1016ca505)

1. **Severity: high.** `src/squad.Ui.Protocol/UiDeliveryCoordinator.cs` `PublishSnapshot` and
   `GetDeliveryState`; `src/squad.Ui.Protocol/TranscriptDeliveryState.cs` `Initial`.
   **Violated behavior:** Requested recovery, queue-overflow recovery, and last-synchronized announcement
   baselines must not regress a sequence or emit announcements already covered. A member that exists in the
   registry only because of a requested position, or that has received incremental delivery but has never
   been synchronized, must not be treated as delivered-at-0 or synchronized-at-0.
   **Root cause:** The old maps stored a member only after an observed delivered or synchronized cursor.
   Missing keys used `GetValueOrDefault(..., role.Sequence)` for announcement start and overflow iterated
   only `myDeliveredTranscriptSequences`. The unified dictionary inserts `Initial` (0, 0, null) for a
   request-only member and keeps that entry after `ConsumingRequestedPosition`. `PublishSnapshot` then
   builds `lastSynchronizedSequences` from every entry and, on overflow, replaces a requested baseline
   with `(0, 0)` when `DeliveredSequence` is still 0.
   **Required outcome:** Never-synchronized members stay missing from last-synchronized baselines so
   announcement start still falls back to `role.Sequence`. Overflow baselines iterate only members with an
   observed delivered cursor, and must not replace a requested position with `(0, 0)` merely because the
   member has no delivered update yet. Consuming a request must not leave a fake 0/0 cursor that later
   cycles treat as observed. Wire payloads stay unchanged.

### Slice 4: Consolidate transcript archive metadata

**Task:** `consolidate-transcript-archive-state`

**Logical change:** Represent each archived role and entry once while preserving the archive's storage and paging
contract.

**Implementation:**

- Add `RoleArchiveState`, containing a `SortedDictionary<int, ArchivedEntryState>` and the sticky role-level
  truncation flag, and `ArchivedEntryState`, containing retained length, total length, and content-truncated state.
  Enforce non-negative length invariants in their construction and transitions.
- Replace the four parallel indexes in `TranscriptArchive` with one
  `Dictionary<SquadMemberId, RoleArchiveState>`. Append, replace, content append, read, and eviction update or remove
  one entry state instead of coordinating composite keys.
- Preserve oldest-first sorted eviction, sticky role truncation, per-entry truncation-marker behavior, archive file
  names and JSON shape, missing-entry responses, locking, and disposal.

**Acceptance:**

- All scenarios in `TranscriptHistoryPaging.feature` and `TranscriptOversizedContent.feature` pass unchanged.
- Paging remains gap- and duplicate-free; retained/total lengths and content/role truncation flags remain exact
  after append, replace, streaming truncation, and eviction.
- No archive path, persisted metadata shape, or UI protocol payload changes.

### Slice 5: Consolidate fake-provider observations

**Task:** `consolidate-fake-observation-state`

**Logical change:** Give `ObservationJournal` one journal per member and one state object per observation kind.

**Implementation:**

- Add `MemberObservationJournal` with latest session id, latest prompt, and a dictionary of observation kind to
  `ObservationState`; each observation state owns its cloned latest `JsonElement` and count.
- Replace the four parallel dictionaries with one member-keyed dictionary. Keep the ordered lifecycle list and
  protocol-error list journal-wide.
- Preserve snapshot and wait semantics, cloned JSON ownership, count increments, timeout behavior, and the existing
  rule that a disposed session remains the latest observed session.
- Render timeout diagnostics from each member journal without dropping any currently reported lifecycle, prompt,
  generic-observation, or protocol-error data.

**Acceptance:**

- Interaction, abort-ordering, startup/shutdown, session-failure, and handoff scenarios that use the fake-provider
  control transport pass unchanged.
- Repeated observations retain the latest value and exact count; role isolation and undisposed-session detection
  remain unchanged.

### Slice 6: Consolidate raw SDK tool trace state

**Task:** `consolidate-sdk-tool-trace-state`

**Logical change:** Track each SDK tool call with one trace state rather than separate payload and tool-name maps.

**Implementation:**

- Add `ToolTraceState` with optional tool name and previous payload, because start and payload events may arrive in
  either order.
- Replace `myPreviousPayloads` and `myToolNames` with one ordinal-keyed dictionary. Start and payload events create
  or update one state; completion removes that state once after its trace record is assembled.
- Preserve sequence assignment, trace JSON shape, payload-field handling, trace-path behavior, and all five payload
  classifications (`first payload`, duplicate, cumulative snapshot, rewrite/non-prefix change, independent delta).

**Acceptance:**

- Tool start, partial result, progress, completion, and server-tool progress still produce byte-for-byte equivalent
  normalized field values apart from timestamps.
- Completion forgets both correlated values together, while payload-before-start and start-without-payload remain
  valid.
- The Copilot provider and its packaging scenarios pass without introducing a production test hook.

### Slice 7: Consolidate transcript observation fixture state

**Task:** `consolidate-transcript-spec-state`

**Logical change:** Make `BackendScenarioSteps` store one transcript observation state per member.

**Implementation:**

- Add `MemberTranscriptObservationState` for paging frontier, observed page count, latest page, accumulated entries,
  assistant-delta count, and latest synchronized sequence.
- Replace the six member-keyed dictionaries with one member-keyed dictionary and route each step through the
  member's state. Preserve the distinction between "not observed yet" and valid zero/default values used by current
  diagnostics.

**Acceptance:**

- Transcript synchronization, paging, oversized-content, and stream-finalization feature files pass unchanged.
- Per-member page chaining, accumulated-entry reconciliation, delta numbering, and archived-entry sequence checks
  remain isolated when scenarios address more than one member.

### Slice 8: Consolidate coexistence fixture state

**Task:** `consolidate-coexistence-spec-state`

**Logical change:** Make `HostCoexistenceSteps` store one scenario/exit-code lifecycle per project label.

**Implementation:**

- Add `ProjectObservationState` containing the required `BackendScenario` and optional exit code.
- Replace the two label-keyed dictionaries with one ordinal-keyed dictionary. Keep duplicate/missing label behavior,
  shared scenario ownership, and teardown responsibility unchanged.

**Acceptance:**

- `HeadquartersCoexistence.feature` passes unchanged.
- Each label continues to route launch, protocol traffic, shutdown, exit-code assertions, and liveness checks only
  to its own project.

## Done when

- All eight slices are accepted independently and each affected owner has one dictionary per domain identity.
- Request/response kind, interaction lifecycle, transcript sequence, archive length/truncation, observation count,
  and per-project lifecycle invariants are valid by construction and changed only through their owning state type.
- Existing public protocol payloads, diagnostics, cancellation behavior, transcript retention behavior, archive
  format, and trace shape remain unchanged.
- The relevant black-box Gherkin scenarios pass after every slice, and the stable architecture documentation still
  accurately describes the resulting ownership boundaries.
