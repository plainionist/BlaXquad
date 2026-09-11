---
title: Establish a role-centered application domain model
priority: 30
---

# Establish a role-centered application domain model

## Problem

The application domain is organized partly around roles and partly around squad-wide managers that each maintain
their own role-keyed state. `AgentRoleState` and `RoleTranscriptState` represent one role, but interactions,
operations, sessions, event projection, snapshots, and command serialization are coordinated above them by
`SquadViewModel` and several multi-role collaborators.

This makes the code describe one squad containing several parallel dictionaries more strongly than it describes a
squad containing several independent roles. The repeated role keys are compensating for a missing ownership boundary.
They also make role-local invariants span classes, locks, and asynchronous control paths.

The problem is not simply that the implementation uses locks. Small locks remain appropriate for atomic lifecycle
admission, provider callbacks, disposal, and transport framing. The problem is that mutable state belonging to one
role does not have one cohesive owner, so synchronization is needed in several places to reconstruct that ownership
at runtime.

This issue documents the current model and defines a target domain model. It intentionally does not prescribe
implementation slices or migration order.

## Current domain model

### Squad-wide application owner

`SquadViewModel` is currently both the UI-facing application facade and the de facto aggregate for all live roles. It
owns:

- the role-to-`AgentRoleState` dictionary;
- the role-to-provider-session dictionary;
- one command channel shared by every role;
- accepted-command tracking for shutdown;
- the squad-wide `PendingInteractionRegistry`;
- the squad-wide `RoleOperationCoordinator`;
- event projection and transcript publication;
- prompt, interaction-response, abort, failure, readiness, and shutdown orchestration; and
- assembly of state and transcript snapshots.

The shared command channel serializes short state transitions, but it accepts opaque `Func<Task>` work. Some queued
work awaits provider I/O. A slow interaction response can therefore hold the shared loop and delay event projection
for unrelated roles even though provider operations themselves are intended to remain independent between roles.

### Role state and transcript

`AgentRoleState` is already role-scoped. It owns status, activity, error, model, usage, and event-count state plus one
`RoleTranscriptState`. Both objects share one lock so an event can update role state and transcript state atomically.

`RoleTranscriptState` is also already role-scoped. It owns retained entries, stream buffers, transcript sequence,
tool-call correlation, retention protection, and access to archived history. Its storage dependency is a shared
`TranscriptArchive` whose operations are keyed by role.

This is the strongest existing domain boundary, but other role-local concepts are not attached to it.

### Pending interactions

`PendingInteractionRegistry` is one squad-wide object containing four role-keyed maps:

- permissions;
- input requests;
- elicitations; and
- protected transcript entries.

Its keys combine role and request ID. Most methods receive a role even though every request already belongs to exactly
one role. Compatibility overloads without a role search all roles and must reject ambiguous request IDs.

An interaction request and the transcript entry kept alive for it form one domain concept, but they are stored in
different collections. Registering an interaction, protecting its transcript entry, removing or restoring it around
a provider response, and eventually unprotecting the entry therefore cross `AgentEventProjector`,
`PendingInteractionRegistry`, `AgentRoleState`, `RoleTranscriptState`, and `SquadViewModel`.

### Role operations

`RoleOperationCoordinator` is one squad-wide object that recreates a separate operation state machine for each role
through parallel dictionaries and sets. It owns per-role prompt and provider-operation semaphores, active cancellation
sources, abort leader/follower completion, event invalidation, failed aborts, and failed roles.

The role's visible failure and working state live in `AgentRoleState`, while the operational meaning of failure,
invalidation, and cancellation lives in `RoleOperationCoordinator`. A role failure or abort consequently requires
coordinated mutations in multiple owners.

### Event projection

`AgentEventProjector` receives an `AgentRoleState` but also depends on the squad-wide interaction registry. Projecting
one provider event can mutate role status, transcript state, pending interactions, transcript protection, and UI
publication state through different objects. The caller must know which locks and admission checks make that combined
transition valid.

### Legitimate cross-role owners

Some multi-role objects model genuinely squad-scoped concerns and are not examples of the missing role boundary:

- `SessionRegistry` atomically owns process lifecycle phase, generation identity, command admission, and current
  session leasing;
- the backend runtime owns provider sessions and their teardown as one generation;
- UI delivery owns ordering, coalescing, and recovery for one UI connection;
- durable handoff delivery coordinates senders and recipients;
- a shared transcript store may coordinate files and storage limits; and
- worktree operation scheduling may coordinate several roles that intentionally share one checkout.

These objects need role routing or role-indexed records because their invariants cross role boundaries. Moving them
inside individual roles would duplicate authority or require a distributed agreement protocol.

## Shortcomings

### Ownership is encoded by convention

Role identity is repeatedly passed as a string and reconstructed in dictionary keys. Correctness depends on every
caller routing to matching entries in every manager. Once a call has selected a role, the receiving object can still
read or mutate another role because the type boundary does not prevent it.

The number of role-keyed collections grows whenever another role-local feature is added. Creating, failing, stopping,
or eventually replacing a role requires those collections to remain synchronized manually.

### Role invariants cross synchronization boundaries

The following are single role-domain transitions but currently span owners:

- projecting an interaction request, adding and protecting its transcript entry, and exposing it to the UI;
- claiming an interaction response, delivering it to the provider, restoring it after failure, and releasing its
  transcript protection;
- invalidating events, cancelling the active operation, invoking provider abort, and returning the role to idle;
- marking a role terminally failed, rejecting later work, clearing pending interactions, and updating visible state;
  and
- creating a snapshot that relates role status, transcript position, and pending interactions.

Locks make individual collections safe, but they do not express these transitions as operations on one domain owner.
Snapshot assembly can also observe role-local collections at different moments because it reads them independently.

### Global serialization is stronger than the domain requires

Provider event order is meaningful within one role. There is no corresponding business requirement that a `coder`
event must be ordered against an unrelated `reviewer` event. The global application channel nevertheless places all
role commits on one queue, and opaque asynchronous delegates make it possible to await external I/O while holding
that queue.

This creates avoidable head-of-line blocking and couples per-session event backpressure. It also obscures which work
is a synchronous state transition and which work is a long-running provider operation.

### The public shape follows storage rather than the domain

Pending permissions, inputs, and elicitations are exposed as squad-wide collections and associated with a role by a
property. Interaction completion APIs at the application boundary route by role, but internal APIs can fall back to
searching all roles by request ID. The model therefore treats role identity as optional after the request has already
crossed a role-addressed protocol boundary.

`SquadViewModel` exposes mutable role objects and assembles snapshots by reaching through several owners. It acts as
state container, command router, concurrency coordinator, provider orchestrator, and presentation adapter, giving it
several independent reasons to change.

### Per-role lifecycle is difficult to reset coherently

Role state is expected to survive or reset differently across abort, session failure, shutdown, and a future session
generation replacement. Because transcript state, pending interactions, operation invalidation, failure state, and
session identity have different owners, there is no single operation that can establish a well-defined role state for
a new generation without coordinating those owners externally.

## Target domain model

The application domain should model a squad as a directory of independently owned roles. The role directory is the
only application-domain collection keyed by role. Once routing selects a role, all role-local operations use that
role's objects without another role parameter or another role-keyed collection.

Names below are conceptual. This issue defines responsibilities and cardinality, not final type names.

```text
Squad application facade
|
`-- Role directory: role identity -> role processor
    |
    `-- Role processor (one per role, sole mutable owner)
        |
        `-- Role aggregate
            |-- role identity and generation association
            |-- projected agent state and readiness
            |-- transcript
            |   |-- retained entries and stream state
            |   |-- tool-call correlation
            |   `-- retention protection
            |-- pending interactions
            |   |-- permissions
            |   |-- input requests
            |   `-- elicitations
            `-- operation state
                |-- prompt serialization
                |-- active operation cancellation
                |-- abort state and event invalidation
                `-- terminal failure state
```

### Role aggregate

Each configured role has exactly one application-domain aggregate for its current generation. It owns all transient
state whose identity and lifecycle are defined by that role:

- projected provider status, activity, error, model, effort, and usage;
- transcript entries, streams, tool correlation, sequence, retention, and role-bound archive access;
- pending permissions, inputs, and elicitations;
- the relationship between a pending interaction and its protected transcript entry;
- prompt and provider-operation admission for the role;
- active-operation cancellation and abort coordination;
- event invalidation and generation checks; and
- role-local failure and reset behavior.

An interaction is stored as one role-local value containing its request and optional protected transcript entry. Its
request ID is unique only within that role. The role aggregate does not need composite role/request keys, and removing
or restoring an interaction cannot affect another role.

The transcript remains a cohesive nested object because it has substantial independent behavior. It is nevertheless
owned by the role aggregate, and interaction protection is changed as part of the same serialized role transition.
A shared archive implementation may remain behind a role-bound archive handle so transcript code does not repeatedly
route by role.

### Role processor

Each role has one logical processor that is the sole mutable accessor to its aggregate. It receives typed role
commands and provider events and commits them in role order. Different role processors can progress independently;
the model defines no total order across roles.

The processor must distinguish state transitions from external operations. It may initiate provider I/O, but it does
not await long-running provider work while preventing later role-state messages from being considered. Completion,
failure, and cancellation return as typed messages carrying the operation and generation identity required to reject
stale results.

This is actor-style ownership, but it does not require an actor framework and it does not require every boundary to
be lock-free. A channel-backed processor is one possible implementation. The domain requirement is one serialized
mutable owner per role, explicit messages, bounded failure/backpressure behavior, and no implicit cross-role
serialization.

### Role snapshots and publications

Mutable role state is not exposed outside its processor. Each committed transition produces or makes available an
immutable role snapshot containing the role's visible status, capabilities, usage, and pending interactions. Transcript
updates retain their explicit per-role sequence.

The squad snapshot is a composition of immutable role snapshots. It need not represent one globally atomic instant
across independent roles, but each included role snapshot must be internally consistent. UI publication remains a
connection-level concern and may coalesce snapshots and transcript updates without becoming an authoritative domain
owner.

### Squad and runtime boundaries

The squad-level application facade routes each external command or provider event to one role processor and composes
read models. It does not own parallel dictionaries for role-local interactions, operations, transcripts, or failures.

The host lifecycle authority continues to own process phase, backend generation, session registration, and atomic
session leasing. Provider session objects and provider-side interaction completion sources remain owned by the backend
adapter; they are resources used by role operations, not application-domain state to merge into the role aggregate.

Cross-role resource constraints also remain explicit external coordinators. In particular, roles sharing a worktree
must acquire an execution-group lease from one worktree-level authority. That lease constrains when a role may invoke
the provider without merging the participating roles' transcripts, interactions, or failure state.

## Required invariants

- Every provider event, user command, interaction response, abort, and operation completion is addressed to exactly
  one role at the squad boundary.
- After routing, role-local APIs do not accept a role parameter and role-local state holders contain no role-keyed
  dictionaries.
- One role processor is the only writer of its aggregate; callers receive immutable values or snapshots.
- Role status, transcript mutation, pending-interaction mutation, transcript protection, operation state, and failure
  state change as one ordered role transition where the domain operation relates them.
- Provider events and transcript updates preserve their existing order within a role. No ordering dependency is
  introduced between unrelated roles.
- A blocked provider operation for one role cannot block state projection, interaction handling, or snapshots for
  another role.
- Same-role prompts and provider operations retain the required serialization, cancellation, abort coalescing, and
  stale-event rejection behavior.
- Asynchronous results carry enough role, generation, and operation identity to prevent an old operation from
  mutating replacement state.
- Session lifecycle and command admission retain one squad-level authority; a role processor cannot outlive or
  bypass the generation that owns its provider session.
- Transcript persistence and UI delivery may share infrastructure, but neither becomes an alternate mutable owner of
  role-domain state.

## Acceptance criteria

- The application model has one cohesive mutable owner per role for projected state, transcript, pending
  interactions, operation coordination, invalidation, and role-local failure.
- `SquadViewModel` or its replacement is limited to application routing, read-model composition, and UI-facing
  commands; it is not the mutable aggregate for all roles.
- `PendingInteractionRegistry` is replaced by role-owned interaction state keyed only by request ID, with transcript
  protection represented as part of the interaction relationship.
- `RoleOperationCoordinator` no longer recreates role instances through squad-wide role-keyed maps; its role-local
  responsibilities belong to the corresponding role owner.
- Event projection mutates only the addressed role aggregate and has no dependency on a squad-wide mutable
  interaction registry.
- Public protocol behavior remains role-addressed and unchanged unless a separate issue explicitly changes the
  protocol contract.
- Existing guarantees for transcript ordering and atomic publication, same-role command serialization, cross-role
  concurrency, interaction routing, abort behavior, failure isolation, readiness, and shutdown remain observable
  through the black-box acceptance suite.
- Locks that remain protect genuinely shared boundary state or short resource-management invariants; no requirement
  exists to remove synchronization merely to call the design actor-based.

## Non-goals

- Selecting an actor framework or requiring a distributed actor runtime.
- Eliminating all locks, semaphores, channels, tasks, or concurrent collections.
- Moving process lifecycle, backend ownership, session admission, UI connection delivery, handoff delivery, or
  shared-worktree scheduling into individual roles.
- Redesigning the UI protocol, transcript wire format, provider SPI, or durable archive format.
- Implementing shared-worktree scheduling described by issue 023.
- Defining implementation slices, migration order, temporary compatibility adapters, or final source-file names.