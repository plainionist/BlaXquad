---
title: Refactor SquadViewModel responsibilities
priority: 10
---

# Refactor SquadViewModel responsibilities

`SquadViewModel` is the central coordination point, but it currently also owns several independent implementation
concerns. Its size makes changes to prompts, aborts, interactions, event rendering, and snapshots affect the same class.

## Responsibilities currently combined

- serialized command/event-loop execution;
- role command admission, locking, cancellation, and abort coordination;
- pending permission, input, and elicitation state;
- projection of every `AgentEvent` into role and transcript state;
- human-readable tool and subagent descriptions;
- protocol snapshot construction; and
- transcript archive lifetime.

## Refactoring direction

Keep `SquadViewModel` as the application-facing coordinator and authoritative serialized mutation boundary. Extract only
the responsibilities with their own state or reason to change:

1. A role-operation coordinator owns per-role prompt serialization, active operation cancellation, abort completion,
   and invalidation state.
2. A pending-interaction registry owns permission/input/elicitation lookup, uniqueness, removal, and the association to
   protected transcript entries.
3. An agent-event projector owns event-to-role/transcript mutations and the pure formatting rules for tools, reads,
   subagents, and model context limits.

The event loop must remain the commit boundary: extracted collaborators must not create competing locks or mutate role
state from background tasks. Avoid pass-through interfaces whose only purpose is reducing the file length.

## Acceptance criteria

- `SquadViewModel` coordinates commands, snapshots, and notifications without implementing the three concerns above.
- Each extracted type has one cohesive responsibility and is placed in `squad.Core`.
- Session leases are still validated at the serialized mutation point.
- Prompt, abort, interaction, readiness, transcript, and relaunch behavior is unchanged.
- Existing public protocol and `ISquadUi` / `ITranscriptUi` contracts remain unchanged unless a narrower contract is
  required by the extracted owner.
- Existing black-box Gherkin acceptance scenarios remain green; add scenarios only for an observable invariant not
  already covered.

## Why priority 10

This is the largest and most frequently connected class in the application. Clear ownership here reduces risk and
coordination cost for nearly every backend, lifecycle, transcript, and UI change.