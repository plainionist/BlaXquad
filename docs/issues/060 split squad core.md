---
title: Split squad core into meaningful modules
priority: 2
---

`squad.Core` is currently the application-domain assembly, but it contains several different kinds of state and
infrastructure. It owns the UI-facing coordination boundary, agent role state, transcript retention and archival,
session lifecycle, and handoff delivery. The existing refactor issues describe important extractions inside these
types; this issue defines which concerns deserve module or assembly boundaries and what the actual repository core is.

## Responsibilities currently combined

### Application coordination

`SquadViewModel` is the application-facing coordinator. It owns the serialized command/event loop, role admission,
pending interactions, event projection, snapshots, notifications, and shutdown of the in-memory application state.

### Role and session state

`AgentRoleState` owns role identity and current observable session status. `SessionRegistry` owns session registration,
active-session lookup, shutdown admission, and lifecycle safety rules.

### Transcript state and storage

`AgentRoleState`, `TranscriptEntryBuffer`, and `TranscriptArchive` together implement retained transcript entries,
streaming assistant/reasoning content, tool output updates, protected entries, truncation, retention limits, paging,
and temporary on-disk archive storage.

### Agent event projection

`SquadViewModel` translates provider events into role state and transcript updates, including tool/read/subagent
descriptions and protocol-facing formatting. This is application-domain behavior, not a generic utility.

### Handoff delivery

`HandoffDeliveryService` and `InProcessHandoffPoller` scan outboxes, write inbox artifacts, recover pending work, move
sent/failed files, and notify active roles. This is a filesystem-backed delivery adapter that uses the handoff protocol
defined by `squad.Agent.Handoff`.

### Cross-cutting contracts

`IHandoffPump`, `IRoleNotifier`, `AgentRoleSnapshot`, and the public methods of `SquadViewModel` form contracts between
the application core, the host, the UI, and provider adapters.

## What is really the core?

The enduring core of this repository is the authoritative application state machine for a squad:

- which roles exist and what their current session state is;
- which commands and interactions are admitted;
- how provider events become observable role and transcript state;
- how lifecycle transitions stop, drain, and replace sessions; and
- which snapshots and notifications are exposed to the UI.

That is the part that should remain in `squad.Core`. It should not become a generic “everything shared by the
application” library, and it should not own technology-specific runtime composition, Photino, Copilot SDK behavior,
filesystem handoff transport, or Vue protocol presentation.

Logging and design-by-contract helpers may eventually belong in a slim dependency-free common layer if they are used
by several independent modules. Do not add placeholder abstractions before there are real cross-module consumers:

- logging should be a narrow structured contract at the boundary where logging is needed, not an assembly-wide global;
- design-by-contract should contain genuinely reusable invariants and argument checks, not hide lifecycle or domain
  validation; and
- a future `squad.Common` or `squad.Core.Primitives` assembly should be created only when its contents have a clear
  owner and more than one meaningful consumer.

## Proposed module and assembly boundaries

### Keep in `squad.Core`

Keep the application-domain coordination and authoritative state in the existing assembly:

- `SquadViewModel`, refactored as the serialized application coordinator;
- `AgentRoleState`, reduced to role/session status;
- `SessionRegistry`, retained as the single lifecycle and admission authority;
- `AgentRoleSnapshot` and application-facing contracts that describe this state;
- internal `RoleOperations`, `Interactions`, and `Events` modules extracted from `SquadViewModel`.

These modules share the event-loop commit boundary and authoritative mutable state. They should initially be namespaces
or folders inside `squad.Core`, not separate assemblies. Creating assemblies for them would encourage pass-through APIs,
duplicate synchronization, or competing lifecycle authorities.

### Extract to `squad.Core.Transcripts`

Create a separate assembly for the cohesive transcript aggregate when the existing role-state refactor is ready:

- `RoleTranscriptState` extracted from `AgentRoleState`;
- `TranscriptArchive`;
- `TranscriptEntryBuffer`;
- `TranscriptRetentionOptions` and transcript-specific policies.

This assembly owns transcript ordering, streaming, protection, retention, truncation, paging, and archive lifetime as one
invariant-rich subsystem. It may depend on the transcript contracts in `squad.Ui.Abstractions` or, if those contracts
are made domain-neutral, on a smaller transcript contract assembly. It must not depend on `SquadViewModel`, the host,
the Copilot SDK, or Photino.

### Extract to `squad.Core.Handoffs`

Create a separate assembly for filesystem-backed handoff delivery:

- `HandoffDeliveryService`;
- `InProcessHandoffPoller`;
- `SessionRoleNotifier`, if notification wiring remains part of handoff recovery;
- `IHandoffPump` and `IRoleNotifier`, if they are only used by this delivery boundary.

This assembly may depend on `squad.Agent.Configuration`, `squad.Agent.Handoff`, and a narrow role-notification
contract. It must not become a second session or lifecycle authority. The application core should own the decision to
start, stop, and recover the pump; the handoff assembly should own delivery mechanics.

### Keep event projection as an internal Core module

Extract an internal `AgentEventProjector` from `SquadViewModel`, as described by the existing ViewModel issue, but do
not make it a separate assembly initially. It mutates role and transcript aggregates at the event-loop commit point
and therefore has a close dependency on Core state. Keep it stateless where possible, with formatting helpers beside it.

## Proposed dependency direction

```text
squad-hq / host
        |
        v
  squad.Core --------------------> AgentProvider.Abstractions
        |                           Ui.Abstractions
        |                           Agent.Configuration
        |
        +--> squad.Core.Transcripts
        +--> squad.Core.Handoffs ----> Agent.Handoff
                                    -> Agent.Configuration

squad.Core.Transcripts -----------> transcript contracts only
squad.Core.Handoffs --------------> role notification contract only
```

The following dependency rules are required:

- `squad.Core` remains the only owner of authoritative application lifecycle and command admission.
- `squad.Core.Transcripts` cannot call back into `SquadViewModel`.
- `squad.Core.Handoffs` cannot look up or mutate sessions directly except through its injected notifier contract.
- Provider adapters and host adapters depend on Core contracts; Core does not depend on Copilot SDK or Photino.
- UI protocol serialization remains at the existing UI/application boundary; transcript state must not know Vue details.
- No assembly introduced by this split may create a second lock, event loop, lifecycle registry, or source of truth.

## Implementation plan

### Slice 1: Establish the Core boundary

1. Complete the existing `SquadViewModel`, `AgentRoleState`, and `SessionRegistry` refactor issues in dependency order.
2. Keep the event loop as the single serialized mutation boundary.
3. Define the minimal contracts needed by extracted transcript and handoff modules before moving implementation code.

### Slice 2: Extract transcript state

1. Extract `RoleTranscriptState` from `AgentRoleState`.
2. Move the archive, streaming buffer, retention options, and transcript-only helpers to `squad.Core.Transcripts`.
3. Preserve transcript snapshot, paging, streaming, truncation, protection, and archive cleanup behavior.
4. Add or retain black-box coverage for ordering and state-preservation invariants before changing the assembly boundary.

### Slice 3: Extract handoff delivery

1. Move delivery, polling, and recovery mechanics to `squad.Core.Handoffs`.
2. Inject a narrow notifier rather than referencing the complete ViewModel or session registry.
3. Keep pump startup, stop, recovery, and generation ownership in the application lifecycle owner.
4. Preserve outbox-to-inbox, sent/failed archival, collision handling, multi-recipient delivery, and notification behavior.

### Slice 4: Retain only genuine common primitives

1. Inventory actual cross-module logging and contract-checking call sites.
2. Introduce a small dependency-free common assembly only if the same abstraction is needed by independent assemblies.
3. Keep logging and design-by-contract APIs narrow and explicit; do not move domain rules into a generic helper package.

### Slice 5: Verify and simplify references

1. Update architecture tests to assert the new dependency graph and the single Core lifecycle authority.
2. Remove obsolete project references and namespaces.
3. Run focused transcript, interaction, lifecycle, handoff, startup, relaunch, and shutdown acceptance scenarios, then the
   complete build and test suite.

## Acceptance criteria

- `squad.Core` has a clearly documented application-domain responsibility: authoritative role/session state,
  admission, event projection, lifecycle coordination, and UI-facing snapshots.
- Transcript retention, streaming, archival, paging, and truncation have one cohesive owner in
  `squad.Core.Transcripts`.
- Handoff polling, recovery, filesystem delivery, and notification wiring have one cohesive owner in
  `squad.Core.Handoffs`.
- `RoleOperations`, `Interactions`, and `Events` are internal Core modules unless a concrete independent assembly
  boundary is demonstrated.
- There is exactly one event-loop commit boundary and one session/lifecycle authority.
- No Core module depends on Copilot SDK, Photino, hosting adapters, or Vue implementation details.
- Public snapshot and transcript protocol shapes remain unchanged unless a separate issue explicitly changes them.
- Logging or design-by-contract code is added only where it has real cross-module reuse and a narrow, testable contract.
- Existing black-box Gherkin scenarios for transcript, interaction, handoff, lifecycle, relaunch, startup, and shutdown
  remain green.

## Relationship to existing issues

This issue is the assembly-level map and should be implemented through the focused refactors already documented in:

- `010 refactor squad view model.md`;
- `030 refactor agent role state.md`; and
- `040 refactor session registry.md`.

Those issues should remain responsible for preserving their respective behavioral invariants. This issue decides which
resulting owners stay inside Core and which deserve an independent assembly.