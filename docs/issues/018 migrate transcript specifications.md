---
title: Migrate transcript and usage specifications
priority: 18
---

# Migrate transcript and usage specifications

This issue implements the [backend test strategy](../manual/test-strategy.md).

## Goal

Rewrite transcript, tool activity, usage, retention, paging, and archived-entry scenarios to publish provider events
through the fake provider and observe the real UI JSON protocol.

Migrate the user-visible transcript scenarios from `ViewModel.feature` and
`ArchivedEntryReconstruction.feature`. Preserve presentation and recovery behavior; remove assertions that expose
archive offsets, buffers, journals, or exact reconstruction inputs as implementation details.

## Acceptance criteria

- Assistant and reasoning streams produce correctly ordered and finalized transcript output.
- Tool start, progress, output, completion, concurrent correlation, file-read summaries, subagent activity, skill
  activity, and suppressed plumbing retain their supported user-visible representation.
- Transcript entry sources and protocol field names remain compatible with the dashboard contract.
- Context and accumulated usage appear in published state without stale updates overwriting newer values.
- Retention remains bounded while older entries stay available through supported paging/recovery behavior.
- Oversized and rotated history has an explicit user-visible truncation or unavailable result.
- Pending interactions and active streams retain the context required by supported behavior.
- Scenarios consume snapshots and transcript protocol messages rather than `AgentRoleState`, `RoleTranscriptState`,
  archive paths, entry buffers, or journal objects.
- Scenarios that report exact archive reconstruction inputs are deleted.
- Atomicity and ordering are asserted through absence of missing, duplicated, or reordered protocol output rather than
  internal locks or callbacks.

## Implementation plan

All slices run the published `squad-hq --ui stdio` process with the fake provider and consume only semantic
operations exposed by `BackendScenario`, its role controller, and the headless UI client. Test support may decode the
wire protocol into test-owned values, but Gherkin and step definitions must not expose raw JSON, production object
graphs, transcript storage, or archive files. Migrate and delete each covered `ViewModel.feature` scenario in the same
slice so old and new specifications do not coexist. Each slice includes its focused fake-provider/UI support,
acceptance scenarios, and obsolete white-box scenario and binding cleanup.

### Slice 1 [done]: Preserve transcript protocol shape and entry sources

1. Introduce the process-boundary transcript feature and only the semantic fake-provider and headless-UI operations
   needed to observe synchronization and incremental transcript messages.
2. Publish user, harness, assistant, reasoning, and system entries and assert their source and content through typed
   test-owned values.
3. Assert the dashboard contract fields for synchronization and updates: source, content, sequence, operation, and
   index. Keep raw envelopes and JSON field access inside the headless client.
4. Remove the covered field-name, source, system-message, and basic snapshot transcript scenarios and bindings from
   `ViewModel.feature` and `ViewModelSteps`.

**Slice acceptance:** After the real UI-ready handshake, a headless client observes each supported transcript source
with the dashboard-compatible source, content, sequence, operation, and index fields, without loading production
objects or parsing raw JSON in steps.

**Status: complete (926705097f).** `src/squad.Specs/Features/TranscriptProtocolShape.feature` covers slice 1
through the process boundary: after the real `ui.ready` handshake, a headless client observes user, harness,
assistant, reasoning, and system sources on `transcript.update` with dashboard-compatible source, content, sequence,
operation, and entry index, and then observes one `transcript.synchronize` message that carries all five sources
with content, a real sequence, and strictly increasing entry indices. Harness content is asserted as the production
`Session started.` line rather than mere source presence. `HeadlessUiClient`/`BackendScenario` gained typed
transcript update and synchronization observations that keep envelope framing and JSON parsing private;
`FakeAgentSession`/`FakeProviderControlServer`/`BackendScenarioAgent` gained assistant and system-message emits.
The covered ViewModel scenarios ("UI snapshots preserve transcript entry field names", "Transcript entries preserve
their sources", "System activity remains visible in the transcript") and their orphaned bindings were removed.
The follow-up `926705097f` closes the review findings that independent first-match synchronize waits could pass
against different messages including the handshake snapshot, and that harness updates were not content-asserted.

### Slice 2 [done]: Preserve assistant and reasoning stream finalization

1. Add semantic assistant/reasoning delta, final-message, and idle operations to the fake-provider controller as
   needed.
2. Prove consecutive deltas aggregate into one entry, snapshot publication does not end aggregation, a final message
   replaces its streamed draft without removing earlier entries, and idle ends a reasoning stream before the next
   delta.
3. Assert complete transcript entries and protocol updates, then remove the corresponding assistant/reasoning stream
   scenarios and bindings from `ViewModel.feature`.

**Slice acceptance:** Assistant and reasoning deltas aggregate into the correct entry, final or idle events close only
that stream, and previously published transcript entries remain intact.

**Status: complete (a11dc83db9).** `src/squad.Specs/Features/TranscriptStreamFinalization.feature` covers slice 2
through the process boundary: consecutive assistant deltas publish `append` then `append-content` on the same entry
index even after a mid-stream `transcript.synchronize`; a final assistant message publishes `replace` on that index
without dropping the earlier user entry; a final reasoning message replaces its streamed draft the same way; and idle
closes a reasoning stream so the next delta starts a new entry, proven by different entry indices and a synchronize
snapshot that contains both reasoning entries. `HeadlessUiClient` gained operation-based update observation so
`append-content` continuations (which carry no source) can be waited on; synchronization assertions require the
expected source/content set in one message rather than independent first-match waits. The six covered ViewModel
scenarios (assistant delta aggregation, snapshot-mid-stream, final reasoning replaces draft, idle finalizes a
reasoning stream, final assistant messages preserve history, final assistant messages replace streamed deltas) and
the orphaned final-reasoning ViewModel binding were removed.

### Slice 3 [done]: Preserve transcript ordering across synchronization races

1. Extend the headless client with a bounded semantic observation that combines initial synchronization with updates
   after its high-water mark.
2. Publish a known ordered sequence before and after synchronization and in a concurrent burst through the fake
   provider.
3. Assert the reconstructed transcript and update sequence contain every entry exactly once and in order.
4. Remove the white-box ordering, atomic-publication, journal-capacity, callback, and Photino assertions and their
   obsolete bindings.

**Slice acceptance:** A real client reconstructs the ordered transcript without missing or duplicated updates when
publication overlaps initial synchronization, without publication pauses, locks, callbacks, or journal inspection.

**Status: complete (22e6fdc775).** `src/squad.Specs/Features/TranscriptSynchronizationOrder.feature` covers slice 3
through the process boundary: a mid-stream `transcript.synchronize` that captured only the first assistant delta is
reconciled with later `append-content` and `replace` updates into the full ordered user+assistant transcript; and a
synchronize request fired concurrently with a five-message system burst is reconciled so each burst entry appears
exactly once regardless of how much the snapshot captured. `HeadlessUiClient.WaitForReconciledTranscriptAsync` seeds
from the latest synchronize high-water mark and replays subsequent updates by sequence, without publication pauses,
locks, callbacks, or journal inspection. The three white-box ViewModel scenarios (ordering/stream semantics,
atomic publication via paused callback, high-water-mark journal replay) and their Photino/journal-capacity bindings
were removed.

### Slice 4 [in progress]: Preserve single-call tool output aggregation

1. Add semantic tool-start, cumulative-output, incremental-output, rewritten-snapshot, and completion operations to
   the fake-provider controller as needed.
2. Prove each supported output mode updates one transcript entry, repeated cumulative content is not duplicated, and
   rewritten snapshots replace superseded output.
3. Remove the covered cumulative, incremental, snapshot-replacement, and streaming-output scenarios and bindings from
   `ViewModel.feature`.

**Slice acceptance:** All output updates for one tool call produce one correctly aggregated transcript entry with no
duplicated or superseded content.

**Status: changes requested (5d0339efe6)**

#### Review findings on 5d0339efe6

**Finding 1 — medium**

- **Location:** `src/squad.Specs/Features/TranscriptToolOutputAggregation.feature`,
  `src/squad.Specs/StepDefinitions/BackendScenarioSteps.cs`
  (`Then the transcript synchronization for role {string} includes an entry with source {string} and content {string}`).
- **Violated behavior:** Slice 4 requires every output update for one tool call to produce one correctly aggregated
  transcript entry with no duplicated or superseded content. The removed ViewModel scenarios asserted exactly one
  `tool` entry, and the console-activity scenario asserted the transcript had no entry `Build succeeded`.
- **Root cause:** Consecutive observed updates sharing an entry index, plus a synchronize `Any(...)` include, do not
  prove the snapshot contains only that tool entry. An extra leftover tool entry — a superseded snapshot, a duplicated
  cumulative item, or the completion summary `Build succeeded` as its own entry — still passes.
- **Required outcome:** After aggregation, observe one synchronize message and assert it contains exactly one `tool`
  entry with the expected aggregated content. For the completion cases, also prove `Build succeeded` is absent.

### Slice 5 [pending]: Preserve concurrent tool-call correlation

1. Publish interleaved output for two concurrently active tool-call IDs through the fake provider.
2. Assert through transcript updates that each output fragment reaches only its matching tool entry and retains
   per-call order.
3. Remove the corresponding concurrent-correlation scenario and obsolete bindings from `ViewModel.feature`.

**Slice acceptance:** Interleaved concurrent tool calls remain separate, and each resulting transcript entry contains
only its own ordered output.

### Slice 6 [pending]: Preserve visible tool lifecycle state

1. Publish tool start, progress, output, and completion through semantic fake-provider operations.
2. Assert the role snapshot exposes the active tool only while it is running, progress does not replace output,
   completion detail is used only when no streamed display output exists, and active state clears on completion.
3. Remove the covered active-state, progress, and completion-fallback scenarios and bindings from
   `ViewModel.feature`.

**Slice acceptance:** The UI protocol exposes correct active-tool state and stable display output throughout one tool
call, including completion with and without prior output.

### Slice 7 [pending]: Preserve ordinary tool command presentation

1. Publish known command arguments, unrecognized arguments, and ordinary tool names containing read-like words.
2. Assert known commands are decoded, unknown arguments remain available in their supported representation, and
   ordinary tools remain tool entries rather than being classified as file reads.
3. Remove the corresponding command-formatting and tool-name scenarios and bindings from `ViewModel.feature`.

**Slice acceptance:** Command tools retain their supported summaries, unknown arguments are not lost, and names alone
do not misclassify an ordinary tool as a file read.

### Slice 8 [pending]: Preserve file-read summaries without content leakage

1. Publish file-read starts and completions for path-only, ranged, and whole-file reads.
2. Assert transcript updates show the path, requested range, or derived whole-file line count as applicable while
   suppressing file contents and completion payloads.
3. Remove the covered file-read and file-view scenarios and bindings from `ViewModel.feature`.

**Slice acceptance:** File-read entries identify what was read, including ranges or line counts, but never publish the
file contents or completion payload through the transcript protocol.

### Slice 9 [pending]: Preserve subagent presentation while suppressing control plumbing

1. Publish semantic subagent activity with complete, partial, and absent display metadata.
2. Assert the supported fallback labels and metadata in transcript updates and prove subagent control tools never
   appear as ordinary tool entries.
3. Remove the subagent metadata and plumbing scenarios and bindings from `ViewModel.feature`.

**Slice acceptance:** Users see one meaningful subagent activity entry with supported metadata fallbacks and no
`task`, `read_agent`, or `list_agents` plumbing.

### Slice 10 [pending]: Preserve visible skill activity while suppressing skill plumbing

1. Publish semantic skill application and discovery events plus the underlying skill tool activity.
2. Assert skill application and discovery remain visible while the skill control call and file contents remain
   absent from transcript updates.
3. Remove the skill application, discovery, and plumbing scenarios and bindings from `ViewModel.feature`.

**Slice acceptance:** Skill use remains understandable in the transcript without exposing control-tool traffic or
skill file contents.

### Slice 11 [pending]: Preserve monotonic context and accumulated usage

1. Extend `ActiveUsageRefresh.feature` and typed snapshot observations to cover context tokens and accumulated AIC
   usage together while a role is working and after it becomes idle.
2. Publish a newer checkpoint followed by a stale checkpoint and distinguish newly published snapshots from older
   matching snapshots.
3. Assert stale provider data never overwrites the latest values, then remove the three corresponding usage scenarios
   and bindings from `ViewModel.feature`.

**Slice acceptance:** Published state reports the latest context and accumulated usage while working and idle, and a
late stale checkpoint cannot regress either value.

### Slice 12 [pending]: Retain pending interaction context

1. Create a pending interaction through the fake provider, then publish enough transcript activity to cross the
   production live-retention boundary.
2. Assert the interaction remains in published state, its transcript context remains recoverable through the UI
   protocol, and the user can answer it successfully.
3. Remove the white-box pending-interaction retention scenario and obsolete bindings from `ViewModel.feature`.

**Slice acceptance:** Retention does not remove the published or recoverable context required to answer a pending
interaction.

### Slice 13 [pending]: Retain active stream context

1. Start an assistant stream, cross the production live-retention boundary with unrelated activity, and continue the
   stream; repeat for reasoning only when the same setup and assertions can share the path.
2. Assert the active entry remains present and receives the later delta instead of creating or modifying another
   entry.
3. Remove the white-box active-stream retention scenario and obsolete bindings from `ViewModel.feature`.

**Slice acceptance:** Live retention preserves the entry required to continue an active assistant or reasoning stream
without splitting, losing, or misrouting later content.

### Slice 14 [pending]: Page retained transcript history

1. Extend the headless UI client with semantic transcript synchronization and previous-page requests while keeping
   request envelopes and storage coordinates private.
2. Publish enough entries to cross the production live-retention and page boundaries.
3. Assert live synchronization is bounded and older available entries page back in order; combining pages with live
   updates must not introduce gaps or duplicates.
4. Migrate the supported available-entry behavior from `ArchivedEntryReconstruction.feature` and remove the covered
   paging, retention, and cleanup scenarios and bindings from `ViewModel.feature`.

**Slice acceptance:** The UI protocol returns bounded live history and all still-available older entries in order,
with temporary history removed when the process workspace is cleaned up.

### Slice 15 [pending]: Report oversized transcript content explicitly

1. Publish entries and streaming content that cross the production per-entry and announcement limits without adding
   configurable test capacities.
2. Assert the UI protocol reports a stable truncated result, keeps live publication bounded, and does not silently
   present partial content as complete.
3. Remove the covered oversized-entry, announcement-bound, and exact-limit streaming scenarios and bindings from
   `ViewModel.feature`.

**Slice acceptance:** Oversized transcript content remains bounded and is explicitly identified as truncated at a
consistent sequence through the supported UI protocol.

### Slice 16 [pending]: Report rotated transcript history as unavailable

1. Publish enough history to cross the production archive boundary and request an entry that has rotated out.
2. Assert the UI protocol returns an explicit unavailable result at the current sequence while newer archived entries
   remain page-accessible and ordered.
3. Remove the covered archive-bound and rotated-content scenarios and obsolete bindings from `ViewModel.feature`.
4. Delete `ArchivedEntryReconstruction.feature`, including the obsolete exact-reconstruction-input outline, after its
   supported available and unavailable outcomes have been migrated.

**Slice acceptance:** Bounded archive rotation preserves available recent history and reports older missing content as
unavailable without exposing offsets, paths, capacities, or reconstruction inputs.
