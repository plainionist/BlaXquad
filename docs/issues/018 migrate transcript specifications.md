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
slice so old and new specifications do not coexist.

### Slice 1: Specify transcript streaming through the UI protocol

1. Introduce a cohesive transcript feature around the published process, fake-provider controller, and headless UI
   client.
2. Extend the role controller with semantic assistant-delta, final-assistant, reasoning-delta, final-reasoning, and
   system-message operations as required. Extend the UI client with typed, bounded transcript synchronization and
   update observations while keeping envelope framing and JSON parsing private.
3. Cover initial synchronization and incremental updates, including the dashboard field names for entry source,
   content, sequence, operation, and index. Exercise user, harness, assistant, reasoning, and system sources.
4. Prove assistant and reasoning aggregation/finalization, preservation of earlier entries, and idle finalization by
   asserting the complete observed transcript and ordered update stream.
5. Emit updates across initial synchronization and in a concurrent burst to prove that no published entry is missing,
   duplicated, or reordered; do not retain the white-box publication pause, lock, callback, journal-capacity, or
   Photino assertions.
6. Remove the now-covered core transcript scenarios and their obsolete bindings from `ViewModel.feature` and
   `ViewModelSteps`.

**Slice acceptance:** A real headless client reconstructs the same ordered transcript from synchronization plus
incremental messages, sources and protocol fields match the dashboard contract, streamed assistant/reasoning entries
finalize correctly, and boundary races produce each update exactly once without inspecting internal state.

### Slice 2: Specify tool and agent activity presentation

1. Add semantic fake-agent operations needed to publish tool start, progress, output snapshots/chunks, completion,
   subagent activity, and skill activity without constructing provider event records in steps.
2. Cover one-item aggregation for cumulative and incremental output, replacement of rewritten snapshots, correlation
   of concurrent calls by tool-call ID, separation of progress from output, completion fallback output, and active-tool
   state while a call is running.
3. Preserve the supported formatting of known command arguments and unknown arguments, file-read paths and ranges,
   whole-file line counts, and ordinary tools whose names merely contain read-like words.
4. Preserve semantic subagent labels, visible skill application and discovery, and system activity while proving that
   subagent control tools, skill plumbing, and file contents remain suppressed.
5. Assert only transcript updates and published role snapshots, then remove the corresponding tool, subagent, skill,
   and activity scenarios and bindings from `ViewModel.feature`.

**Slice acceptance:** Tool calls remain correlated and update exactly one appropriate transcript entry, supported
summaries and metadata are unchanged, active state clears on completion, and suppressed plumbing never appears in the
UI protocol.

### Slice 3: Specify usage and retained live context

1. Consolidate context-token and accumulated AIC usage behavior at the process boundary, reusing
   `ActiveUsageRefresh.feature` rather than creating duplicate coverage.
2. Extend semantic state-snapshot observations to assert both context and AIC fields together and to distinguish a
   newly published snapshot from an older matching snapshot.
3. Publish newer and then stale usage checkpoints through the fake provider and prove stale data cannot overwrite the
   latest values while the role is working or after it becomes idle.
4. With enough transcript churn to cross the production retention boundary, prove that a pending interaction remains
   published and answerable and that an active assistant or reasoning stream remains present and continues in the
   correct entry.
5. Remove the corresponding usage, pending-interaction-context, and active-stream-context scenarios and bindings from
   `ViewModel.feature`.

**Slice acceptance:** Published snapshots expose current context and accumulated usage monotonically, and retention
never removes state still required to answer a pending interaction or continue an active stream.

### Slice 4: Specify bounded history paging and recovery

1. Extend the headless UI client with semantic transcript-page requests and typed synchronization/page results,
   including explicit truncation and unavailable outcomes; keep request envelopes and storage coordinates private.
2. Drive enough entries and content through the fake provider to cross the real production retention, page, per-entry,
   and archive limits without adding test-only production capacities or timing knobs.
3. Prove that live state remains bounded, older entries are returned in order through supported paging, and
   synchronization plus incremental updates reconstructs history without gaps or duplicates.
4. Prove user-visible behavior for an oversized entry and for history rotated out of the bounded archive: return the
   supported truncated or unavailable result at a consistent sequence rather than asserting offsets, buffer sizes,
   archive paths, or exact reconstruction inputs.
5. Delete `ArchivedEntryReconstruction.feature`, including the scenario outline that reports exact reconstruction
   inputs, after migrating only its supported paging and unavailable-entry behavior. Remove the corresponding
   retention, paging, archive, announcement-bound, and reconstruction scenarios and obsolete bindings from
   `ViewModel.feature` and `ViewModelSteps`.

**Slice acceptance:** Retained and archived history stays within production bounds, available older entries can be
recovered in order through the UI protocol, oversized or rotated content has an explicit stable outcome, temporary
history is cleaned up with the process workspace, and no scenario reads transcript internals or archive storage.
