---
title: Separate role status from transcript state
priority: 30
---

# Separate role status from transcript state

`AgentRoleState` contains two distinct state models: the current role/session status and the complete retained transcript
implementation. Transcript streaming, tool updates, archive paging, protection, announcements, and retention dominate
the class and can change independently from role status.

## Refactoring direction

Extract a `RoleTranscriptState` that owns:

- retained and protected transcript entries;
- assistant and reasoning stream buffers;
- tool-call transcript state;
- archive reads and writes;
- transcript sequence/index allocation; and
- retention, truncation, and announcement policies.

Keep `AgentRoleState` responsible for role identity and current session status: status, errors, activity, model, effort,
usage, context, event count, and the corresponding snapshot. Expose the transcript owner explicitly to the event
projector rather than preserving a broad set of forwarding methods.

Do not split individual transcript algorithms into one-method services. They form one cohesive transcript aggregate and
share ordering and retention invariants.

## Implementation plan

### Slice 1: Extract the in-Core transcript aggregate

**Status: implemented — hand off to reviewer**

This issue changes ownership inside `squad.Core` only. The later move to `squad.Core.Transcripts` remains Slice 2 of
`060 split squad core.md` and must wait for review acceptance of this refactor.

1. Add one internal `RoleTranscriptState` type in `squad.Core`. Move all transcript-owned fields and algorithms from
   `AgentRoleState` into it: retained/protected entries, assistant and reasoning buffers, tool-call correlation,
   sequence/index allocation, archive reads/writes, announcements, retention, truncation, paging, and reconstruction.
   Keep the private tool state nested because it has no independent responsibility.
2. Keep the existing `TranscriptArchive` shared across roles and owned/disposed by `SquadViewModel`. Construct one
   `RoleTranscriptState` per `AgentRoleState` with the role name, archive, retention options, and the role's existing
   synchronization root.
3. Preserve one per-role synchronization boundary. `AgentRoleState` continues to own and expose the existing internal
   `SyncRoot`; transcript reads lock that same root, and event projection mutates role status and transcript state
   together while holding it. Do not add a transcript lock, event loop, background task, or callback into
   `SquadViewModel`.
4. Expose the transcript aggregate explicitly to `SquadViewModel` for snapshot/page/archive reads and event projection.
   Remove transcript mutation forwarding methods from `AgentRoleState`. Retain the public `TranscriptEntries`
   compatibility property as a read-only projection because existing consumers use it.
5. Keep `ActiveTool` as role/session status. Tool transcript operations continue to own tool-call correlation and
   report the currently active tool back to the event projector, which updates `AgentRoleState.ActiveTool` at the same
   serialized commit point. Preserve the existing lifecycle paths that clear active tool state.
6. Update pending-interaction cleanup to unprotect entries through `RoleTranscriptState`. Protected interaction entries
   must remain retained until the corresponding permission, input, or elicitation leaves the pending registry.
7. Keep `RoleTranscriptSnapshot`, `RoleTranscriptPage`, `RoleArchivedTranscriptEntry`, `TranscriptUpdate`, JSON field
   names, update sequencing, and announcement timing unchanged. Do not move files between assemblies or change public
   UI contracts in this slice.
8. Run the focused `ViewModel`, `ArchivedEntryReconstruction`, `SnapshotPublication`, `Context`, and
   `PhotinoUiProtocol` scenarios, followed by the complete build and acceptance suite.

**Slice acceptance**

- `AgentRoleState` contains only role identity/session status, its snapshot, the shared per-role synchronization root,
  the explicit transcript owner, and the compatibility transcript-entry projection.
- `RoleTranscriptState` is the sole owner of transcript and tool-correlation mutable state; `AgentRoleState` has no
  transcript buffers, archive/options fields, retained-entry collections, sequence counters, or retention algorithms.
- A provider event still commits status, active-tool, transcript, sequence, and notification changes atomically from
  the observer's perspective.
- Snapshot materialization during streaming does not finalize or alter a stream.
- Sequence and entry indexes remain monotonic across retention, paging, archive rotation, and reconstruction.
- Assistant/reasoning streaming, cumulative and incremental tool output, concurrent tool correlation, protected
  interactions, truncation markers, disk bounds, reset behavior, and archive cleanup are unchanged.
- No new synchronization authority, pass-through service, protocol shape, or assembly dependency is introduced.

## Acceptance criteria

- Role/session status and transcript state have separate owners.
- `RoleTranscriptState` preserves monotonic sequence and entry indexes across retention and archive paging.
- Streaming assistant/reasoning entries, tool progress, protected interactions, and reset behavior are unchanged.
- Snapshot and archive protocol shapes remain unchanged.
- Existing black-box Gherkin transcript scenarios remain green, including truncation, paging, streaming, and tool output.

## Why priority 30

Transcript behavior is complex and frequently evolving. The split substantially improves local reasoning and enables a
smaller role-state model, with less lifecycle risk than the first two refactors.