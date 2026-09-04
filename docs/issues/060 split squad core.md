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
- `IHandoffPump`; and
- `IRoleNotifier`.

This assembly may depend on `squad.Agent.Configuration`, `squad.Agent.Handoff`, and a narrow role-notification
contract. Narrow `IRoleNotifier` to the recipient role name so notification does not expose delivery configuration.
`SessionRoleNotifier` is the host adapter between that contract and `SessionRegistry`/`SquadViewModel`; place it beside
the `squad-hq` composition code rather than making either extracted assembly depend on the other. `SquadApplication`
continues to own the decision to start, stop, and recover the pump; the handoff assembly owns delivery mechanics.

### Keep event projection as an internal Core module

Extract an internal `AgentEventProjector` from `SquadViewModel`, as described by the existing ViewModel issue, but do
not make it a separate assembly initially. It mutates role and transcript aggregates at the event-loop commit point
and therefore has a close dependency on Core state. Keep it stateless where possible, with formatting helpers beside it.

## Proposed dependency direction

```text
squad-hq / host
        +--> squad.Core --------------------> AgentProvider.Abstractions
        |       |                             Ui.Abstractions
        |       |
        |       +--> squad.Core.Transcripts -> transcript contracts only
        |
        +--> squad.Core.Handoffs ------------> Agent.Handoff
                                               Agent.Configuration
```

The following dependency rules are required:

- `squad.Core` remains the only owner of authoritative application lifecycle and command admission.
- `squad.Core.Transcripts` cannot call back into `SquadViewModel`.
- `squad.Core.Handoffs` cannot reference `squad.Core`, look up sessions, or mutate application state; it can only invoke
  its injected notifier contract with a recipient role name.
- `squad-hq` owns the `SessionRoleNotifier` adapter and handoff-pump lifecycle because it composes the pump,
  `SessionRegistry`, and `SquadViewModel`.
- Provider adapters and host adapters depend on Core contracts; Core does not depend on Copilot SDK or Photino.
- UI protocol serialization remains at the existing UI/application boundary; transcript state must not know Vue details.
- No assembly introduced by this split may create a second lock, event loop, lifecycle registry, or source of truth.

## Implementation plan

The focused refactors remain separate issues and are prerequisites rather than hidden work in this issue:

- `030 refactor agent role state.md` must be accepted before the transcript assembly move.
- `010 refactor squad view model.md` and `040 refactor session registry.md` must be accepted before final boundary
  verification.
- The handoff extraction has no dependency on those refactors and is the first independently reviewable slice.

### Slice 1: Extract filesystem handoff delivery

**Status: complete (27aec057b5)**

1. Add `src/squad.Core.Handoffs/squad.Core.Handoffs.csproj` to `squad.slnx`. Reference only
   `squad.Agent.Configuration` and `squad.Agent.Handoff`.
2. Move `HandoffDeliveryService`, `InProcessHandoffPoller`, `IHandoffPump`, and `IRoleNotifier` from `squad.Core` into
   the new assembly and namespace without changing polling, recovery, delivery, archival, collision, cancellation, or
   failure behavior.
3. Change `IRoleNotifier.NotifyAsync` to accept the recipient role name rather than `RoleRow`. Delivery retains
   `RoleRow` internally for filesystem paths.
4. Move `SessionRoleNotifier` to `squad-hq/Commands`; it implements the narrow notifier by resolving the active role
   through `SessionRegistry` and sending the existing wake message through `SquadViewModel`.
5. Update host/spec project references, namespaces, test doubles, and composition. Remove
   `squad.Agent.Configuration` and `squad.Agent.Handoff` references from `squad.Core`; the current code uses those
   references only for the handoff files being moved.
6. Extend `Architecture.feature` and its step definitions to prove `squad.Core.Handoffs` has only the two allowed
   project dependencies, does not reference `squad.Core`, and `squad.Core` no longer references the handoff or
   configuration assemblies.
7. Run the focused `Delivery`, `Recovery`, `ViewModel`, `Startup`, and `Architecture` scenarios, followed by the
   complete build and acceptance suite.

**Slice acceptance**

- `squad.Core.Handoffs` is the sole owner of filesystem polling, recovery, delivery, and delivery contracts.
- `squad.Core.Handoffs` has no dependency on `squad.Core`, hosting, provider, UI, Copilot SDK, or Photino assemblies.
- `squad-hq` remains the lifecycle/composition owner and is the only production location that bridges handoff
  notification to active Core sessions.
- Existing delivery ordering, durability, fan-out validation, notification-failure, recovery, cancellation, and
  shutdown behavior is unchanged.

### Slice 2: Extract the transcript aggregate

**Status: in progress — `030` Slice 1 accepted at ba6aa836b5; hand off to coder**

1. Add `src/squad.Core.Transcripts/squad.Core.Transcripts.csproj` to `squad.slnx`. Its only project reference is
   `squad.Ui.Abstractions`, which owns the existing transcript entry, update, snapshot, page, archive-entry, and
   announcement contracts.
2. Move `RoleTranscriptState`, `TranscriptArchive`, `TranscriptEntryBuffer`, `TranscriptRetentionOptions`, and
   `ToolCompletionResult` to that project under the `squad.Core.Transcripts` namespace. Keep
   `TranscriptEntryBuffer` and archive implementation methods internal. Put `ToolCompletionResult` in its own source
   file so every source file contains one top-level type.
3. Make only the cross-assembly composition and aggregate API public:
   - `TranscriptArchive` construction, `DirectoryPath`, and disposal;
   - `RoleTranscriptState` construction, transcript reads, event-projection mutations, stream finalization, and
     interaction unprotection;
   - `TranscriptRetentionOptions`; and
   - `ToolCompletionResult`.
   Do not add interfaces or forwarding services around these concrete cohesive owners, and do not expose archive
   storage algorithms to Core.
4. Preserve the accepted synchronization boundary from `030`: `AgentRoleState` creates and owns the per-role lock,
   passes it to `RoleTranscriptState`, and the serialized Core event projector holds that lock while committing role
   status, active-tool, and transcript changes. Transcript read methods lock the same object. Do not add a lock, event
   loop, background task, callback, or `InternalsVisibleTo` coupling in the transcript assembly.
5. Keep `TranscriptArchive` creation, shared lifetime, history-directory exposure, and disposal in `SquadViewModel`.
   Keep `AgentRoleState.Transcript` as the explicit internal aggregate reference and retain its public
   `TranscriptEntries` compatibility projection. The transcript assembly must not reference `AgentRoleState`,
   `SquadViewModel`, or any other Core type; remove the current Core-specific XML reference from
   `ToolCompletionResult`.
6. Update Core/spec project references and namespaces. Remove the transcript implementation files from
   `squad.Core`; do not change the `SquadViewModel` constructor contract, public snapshot/page/archive APIs, serialized
   JSON fields, or UI protocol contracts.
7. Extend `Architecture.feature` and its step definitions to prove:
   - `squad.Core.Transcripts` references only `squad.Ui.Abstractions`;
   - it does not reference `squad.Core`, hosting, handoff, configuration, provider adapters, Copilot SDK, or Photino;
   - `squad.Core` references `squad.Core.Transcripts`; and
   - transcript implementation types are owned by the transcript assembly rather than Core.
8. Run focused `ViewModel`, `ArchivedEntryReconstruction`, `SnapshotPublication`, `Context`,
   `PhotinoUiProtocol`, and `Architecture` scenarios, followed by the complete build and acceptance suite.

**Slice acceptance**

- Transcript ordering, streaming, tool progress, protection, retention, truncation, paging, and archive lifetime have
  one owner in `squad.Core.Transcripts`.
- Sequence and entry indexes remain monotonic across retention and archive paging.
- Core remains the mutation/lifecycle authority; the transcript assembly has no event loop, session registry, or
  callback to the coordinator.
- Provider-event projection still commits role status, active-tool, transcript update, sequence, and notification
  ordering through the one Core event-loop/per-role-lock boundary.
- Public snapshot, paging, archive reconstruction, announcement, JSON, and UI protocol shapes remain unchanged.

### Slice 3: Enforce and document the final Core boundary

**Status: waiting for acceptance of Slice 2 and the `010`/`040` refactor issues**

1. Update the architecture scenario to assert the complete dependency graph and exactly one lifecycle/admission owner.
2. Verify `RoleOperations`, `Interactions`, and `Events` remain internal Core modules and remove obsolete references,
   namespaces, and transitional APIs left by the accepted prerequisite refactors.
3. Update the repository architecture documentation to name Core, Transcripts, Handoffs, host composition, and their
   allowed dependency directions.
4. Inventory logging and contract checks touched by the split. Create no common assembly unless at least two
   independent modules already need the same narrow, dependency-free abstraction; otherwise leave each concern with
   its owner.
5. Run focused interaction, transcript, handoff, lifecycle, relaunch, startup, shutdown, and architecture scenarios,
   followed by the complete build and acceptance suite.

**Slice acceptance**

- The implemented project graph matches this issue and is protected by black-box architecture scenarios.
- There is one event-loop commit boundary and one session/lifecycle/admission authority.
- No placeholder common assembly, pass-through layer, duplicate synchronization boundary, or duplicate source of truth
  was introduced.

## Acceptance criteria

- `squad.Core` has a clearly documented application-domain responsibility: authoritative role/session state,
  admission, event projection, lifecycle coordination, and UI-facing snapshots.
- Transcript retention, streaming, archival, paging, and truncation have one cohesive owner in
  `squad.Core.Transcripts`.
- Handoff polling, recovery, filesystem delivery, and notification invocation have one cohesive owner in
  `squad.Core.Handoffs`; the Core-session notification adapter remains in host composition.
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