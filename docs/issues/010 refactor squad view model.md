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

## Implementation plan

### Slice 1: Extract pending interaction state

**Status: implemented — hand off to reviewer**

1. Add one internal `PendingInteractionRegistry` type under an `Interactions` folder/namespace in `squad.Core`. It owns
   the interaction lock, typed permission/input/elicitation dictionaries, role-scoped key construction, uniqueness
   checks, lookup, removal/restoration, and protected transcript-entry associations.
2. Give the registry explicit typed operations for permission, input, and elicitation requests rather than exposing its
   dictionaries or accepting them back from `SquadViewModel`. Preserve lookup semantics:
   - `(role, request ID)` is unique within each interaction kind;
   - a role-qualified lookup rejects a missing or wrong-role request;
   - a legacy request-ID-only lookup succeeds for exactly one role and rejects zero or ambiguous matches; and
   - duplicate requests produce the existing role-specific error.
3. Keep session response execution, command tracking, retry policy, role-failure decisions, transcript mutation, and UI
   notification in `SquadViewModel`. The registry returns removed/restored requests and protected `(role, entryIndex)`
   associations; it must not reference sessions, `AgentRoleState`, `RoleTranscriptState`, or UI callbacks.
4. Update event handling to register each request and associate its protected transcript entry through the registry.
   Update completion to remove before sending, restore on a non-terminal response failure, and release protection on
   success or terminal role failure exactly as today.
5. Update failed-role, abort, and shutdown cleanup to ask the registry for the interactions/associations it removes,
   then unprotect transcript entries in `SquadViewModel` while holding the existing per-role lock. Never hold the
   registry lock while awaiting a session operation or acquiring a role lock.
6. Keep the public `PendingPermissions`, `PendingInputs`, `PendingElicitations`, and `GetPendingElicitation` members and
   snapshot JSON shape unchanged; they become narrow projections over registry snapshots/lookups.
7. Add one focused black-box `ViewModel` scenario proving that the same request ID can be pending for two roles, a
   role-qualified completion reaches only its owner, and an unqualified ambiguous completion is rejected. Run it with
   the existing failed-role, snapshot interaction state, retained protected context, completion routing,
   wrong-role/late completion, abort, and shutdown scenarios, followed by the complete build and acceptance suite.

**Slice acceptance**

- `SquadViewModel` no longer owns interaction dictionaries, their lock, role-scoped key rules, or protected-entry
  association state.
- `PendingInteractionRegistry` has no session, transcript aggregate, event-loop, notification, or presentation
  dependency and starts no background work.
- Interaction registration/removal and transcript protection preserve their current ordering at the serialized commit
  boundary.
- Same request IDs remain independently addressable across roles, while ambiguous unqualified completion still fails.
- Failed response attempts restore non-terminal requests; successful, aborted, failed-role, and shutdown paths retain
  their existing observable behavior.

### Slice 2: Extract role-operation coordination

**Status: waiting for reviewer acceptance of Slice 1**

1. Add one internal `RoleOperationCoordinator` under a `RoleOperations` folder/namespace. Move the per-role prompt and
   operation semaphores, active-operation cancellation sources, abort completions, invalidation state, failed-abort
   barriers, failed-role state, and their synchronization lock into it.
2. Expose narrow operations for prompt serialization, operation registration/release, role failure, invalidation,
   abort leader/follower coordination, abort success/failure completion, waiting for an in-flight abort, and event
   admission. Use coordinator-owned disposable leases where lock release must be paired; put each independently useful
   top-level lease/result type in its own file.
3. Keep global command admission/draining, session lookup/calls, event-loop enqueueing, role status/transcript mutation,
   and notifications in `SquadViewModel`. The coordinator must not own `IAgentSession`, invoke provider APIs, enqueue
   commands, or mutate `AgentRoleState`.
4. Preserve lock ordering and cancellation semantics: beginning abort invalidates event admission and cancels the
   active local operation atomically; concurrent abort callers share one completion; a failed abort leaves the barrier
   closed; a later prompt waits for the abort and resumes events only after success.
5. Dispose coordinator-owned semaphores during ViewModel disposal without introducing a second lifecycle authority.
6. Run focused prompt serialization, active-prompt cancellation, concurrent/in-flight abort, failed abort, readiness,
   failed role, relaunch, and shutdown scenarios, followed by the complete build and acceptance suite.

**Slice acceptance**

- `RoleOperationCoordinator` is the single owner of per-role operation serialization, cancellation, invalidation, and
  abort state.
- `SquadViewModel` remains the only command admission/event-loop owner and the only component that invokes sessions.
- Prompt, abort, readiness, stale-event suppression, failure, shutdown, and disposal behavior is unchanged.

### Slice 3: Extract agent event projection

**Status: waiting for reviewer acceptance of Slice 2**

1. Add one internal `AgentEventProjector` under an `Events` folder/namespace. Move the complete provider-event switch,
   assistant/reasoning stream projection, transcript entry construction, tool/read/subagent/skill formatting, tool
   classification, argument parsing, and model context-limit rules into it.
2. Inject the accepted `PendingInteractionRegistry` into the projector so interaction events are registered at the
   same commit point. Do not inject `SquadViewModel`, sessions, command queues, notification callbacks, or lifecycle
   services.
3. Keep `SquadViewModel.ApplyEventAsync` responsible for role/session lookup, failed/invalidation/readiness-generation
   admission, acquiring the existing role lock, invoking the projector, publishing `TranscriptChanged` in the current
   order, and scheduling the existing UI refresh priority.
4. Make projection synchronous and deterministic. It may mutate only the supplied `AgentRoleState`, its explicit
   `RoleTranscriptState`, and the interaction registry while the coordinator holds the role lock; it must not acquire
   a competing role lock or start asynchronous/background work.
5. Keep all `AgentEvent` mappings, transcript sources/content, suppressed plumbing tools, read ranges, tool progress,
   concurrent tool correlation, model fallback limits, transcript sequence, and notification timing unchanged.
6. Run focused event/transcript, tool/read/subagent/skill, context/model, interaction, readiness, stale-event,
   snapshot-publication, and `PhotinoUiProtocol` scenarios, followed by the complete build and acceptance suite.

**Slice acceptance**

- `SquadViewModel` contains orchestration and admission but no provider-event switch or formatting rules.
- `AgentEventProjector` is an internal Core module with no session, host, provider-adapter, UI serialization, lifecycle,
  or background-work responsibility.
- There remains one event-loop commit boundary and one per-role synchronization root.
- Public protocol shapes and all observable event ordering remain unchanged.

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