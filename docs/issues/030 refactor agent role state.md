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

## Acceptance criteria

- Role/session status and transcript state have separate owners.
- `RoleTranscriptState` preserves monotonic sequence and entry indexes across retention and archive paging.
- Streaming assistant/reasoning entries, tool progress, protected interactions, and reset behavior are unchanged.
- Snapshot and archive protocol shapes remain unchanged.
- Existing black-box Gherkin transcript scenarios remain green, including truncation, paging, streaming, and tool output.

## Why priority 30

Transcript behavior is complex and frequently evolving. The split substantially improves local reasoning and enables a
smaller role-state model, with less lifecycle risk than the first two refactors.