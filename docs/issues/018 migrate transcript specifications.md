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
