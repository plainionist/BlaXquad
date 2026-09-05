---
title: Align backend assemblies with cohesive modules
priority: 70
---

# Align backend assemblies with cohesive modules

The backend already has good boundaries around the application model, transcripts, provider contracts, UI contracts,
and technology adapters, but three physical assemblies still combine modules with different consumers and reasons to
change:

- `squad.Handoffs` contains both the agent-safe durable queue and the host-only delivery runtime;
- `squad.Photino` contains the native Photino adapter, the technology-neutral UI protocol, transcript delivery, and
  generic process helpers; and
- `squad-hq` contains workspace provisioning, host ownership and control, host runtime lifecycle, and executable
  composition.

Restructure these areas around the class dependency graph while preserving behavior, protocol payloads, file formats,
startup ordering, and lifecycle authority.

## Goals

- Give each library assembly one cohesive responsibility and one primary reason to change.
- Keep both executables as thin interface adapters and composition roots.
- Keep the `squad` agent executable transitively free of host lifecycle, delivery pumps, UI, and provider runtimes.
- Isolate technology-neutral UI protocol behavior from Photino.NET.
- Isolate host ownership and IPC from session runtime lifecycle and workspace provisioning.
- Preserve the authoritative mutation and lifecycle boundaries already established by existing issues.
- Make every extraction independently buildable, testable, publishable, and reversible.

## Non-goals

- Do not change command syntax, output, exit codes, handoff files, transcript ordering, or UI protocol payloads.
- Do not introduce restart, relaunch, generation, or command-admission behavior owned by `restart button.md`.
- Do not split `RoleOperations`, `Interactions`, or `Events` out of `squad.Application`.
- Do not split `CopilotSdkAgentSession` interaction coordination here; that remains owned by
  `050 refactor copilot sdk agent session.md`.
- Do not rename every existing assembly merely for naming consistency.
- Do not create `squad.Common`, `squad.Foundation`, pass-through services, or one-class contract assemblies.
- Do not change frontend source code; the existing browser protocol tests remain compatibility gates.

## Baseline defect to resolve first

The architecture suite is not green after commit `0cae493`, which merged the former handoff-contract project into
`squad.Handoffs`:

- the agent executable now directly references `squad.Handoffs`, while its closure assertion still expects only
  `squad`, `squad.Configuration`, and `squad.Process`; and
- the handoff-delivery assertion expects `squad.Handoffs` to reference itself, although its actual direct project
  references are `squad.Configuration` and `squad.Process`.

Repair these assertions as a standalone characterization change before moving production types. The temporary
baseline must describe the current merged structure accurately and pass; the later handoff split must then replace it
with the final target assertions. Do not let a pre-existing red architecture scenario hide migration regressions.

Also add a startup characterization in which the role collection is empty when `SquadApplication` is constructed and
is populated during pre-start preparation. The test must prove that role initialization happens after preparation,
because the production `Launch` path relies on that ordering even though current test fixtures generally pre-populate
roles.

## Assembly design rules

Logical modules do not automatically become assemblies. Create an assembly only when the module has an independent
consumer set, dependency direction, technology boundary, or lifecycle:

- keep tightly collaborating state-transition modules inside their authoritative aggregate;
- extract adapters and I/O modules that can depend inward through an existing narrow contract;
- keep implementation helpers internal and expose one purposeful facade instead of making classes public merely to
  cross an assembly boundary;
- grant `squad.Specs` narrow `InternalsVisibleTo` access where focused tests require it, but never grant production
  assemblies friendship to bypass a missing API; and
- keep all project references acyclic and reject executable-to-executable or library-to-executable references.

## Target assembly structure

| Assembly                           | Responsibility and owned types                                                                                                                                                | Allowed backend dependencies                                                                                                           |
| ---------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------- |
| `squad.Process`                    | Agent-safe executable discovery and synchronous/asynchronous process execution through `ProcessRunner`, `ProcessResult`, `CliExitException`, and a narrow executable locator. | None                                                                                                                                   |
| `squad.Configuration`              | Repository identity, current-role resolution, configuration loading and validation, and immutable role/topology records.                                                      | `squad.Process`                                                                                                                        |
| `squad.Handoffs`                   | Durable handoff headers, naming, priority, timestamps, sequencing, queue inspection, and atomic queue-state transitions.                                                      | `squad.Process`                                                                                                                        |
| `squad.Handoffs.Delivery`          | Outbox delivery, recovery, polling, and recipient notification through `HandoffDeliveryService`, `InProcessHandoffPoller`, `IHandoffPump`, and `IRoleNotifier`.               | `squad.Configuration`, `squad.Handoffs`                                                                                                |
| `squad.AgentProvider.Abstractions` | Technology-neutral backend/session lifecycle, events, requests, responses, readiness, and runtime factory contracts.                                                          | None                                                                                                                                   |
| `squad.CopilotSdk`                 | Copilot SDK lifecycle, session adaptation, event normalization, telemetry, and provider-specific diagnostics.                                                                 | `squad.AgentProvider.Abstractions`                                                                                                     |
| `squad.Ui.Abstractions`            | Authoritative application-to-UI command/query ports and immutable snapshot, transcript, page, update, and announcement records.                                               | None                                                                                                                                   |
| `squad.Transcripts`                | Per-role transcript mutation, streaming, retention, archive rotation, paging, and reconstruction.                                                                             | `squad.Ui.Abstractions`                                                                                                                |
| `squad.Application`                | The authoritative in-process squad model, including role projection, pending interactions, per-role operation serialization, and `SquadViewModel`.                            | `squad.AgentProvider.Abstractions`, `squad.Ui.Abstractions`, `squad.Transcripts`                                                       |
| `squad.Hosting.Abstractions`       | Narrow process-host lifecycle ports for the window host and sleep inhibitor.                                                                                                  | None                                                                                                                                   |
| `squad.Ui.Protocol`                | Versioned UI envelope parsing and serialization, command routing, snapshot scheduling, transcript delivery, journaling, and recovery behind one protocol facade.              | `squad.Ui.Abstractions`                                                                                                                |
| `squad.Photino`                    | The concrete native window and sleep-inhibition adapter, with Photino.NET and OS-specific implementation details.                                                             | `squad.Hosting.Abstractions`, `squad.Ui.Abstractions`, `squad.Ui.Protocol`, `squad.Process`                                            |
| `squad.Workspaces`                 | Launch-time project layout, configuration materialization, helper discovery, Git worktree preparation, shared-path linking, and handoff-directory initialization.             | `squad.Configuration`, `squad.Process`                                                                                                 |
| `squad.Host.Control`               | Single-host ownership, metadata, locking, named-pipe request handling, readiness queries, shutdown requests, and main-checkout discovery.                                     | `squad.Process`                                                                                                                        |
| `squad.Host.Runtime`               | Process-wide runtime coordination, session registration and observation, handoff participation, readiness exposure, terminal-signal selection, and ordered cleanup.           | `squad.AgentProvider.Abstractions`, `squad.Application`, `squad.Handoffs.Delivery`, `squad.Hosting.Abstractions`, `squad.Host.Control` |
| `squad`                            | Agent-facing command parsing and presentation for context, handoff creation, and task or batch queue transitions.                                                             | `squad.Configuration`, `squad.Handoffs`, `squad.Process`                                                                               |
| `squad-hq`                         | Headquarters command parsing and composition of workspace, control, runtime, provider, and desktop adapters.                                                                  | All concrete modules required for composition, with no reverse reference                                                               |

## Target dependency direction

```text
squad
  +--> squad.Configuration ---------> squad.Process
  +--> squad.Handoffs --------------> squad.Process
  +--> squad.Process

squad-hq
  +--> squad.Workspaces ------------> squad.Configuration
  |                                  squad.Process
  +--> squad.Host.Control ----------> squad.Process
  +--> squad.Host.Runtime ----------> squad.Host.Control
  |                                  squad.Handoffs.Delivery
  |                                  squad.Hosting.Abstractions
  |                                  squad.Application
  |                                  squad.AgentProvider.Abstractions
  +--> squad.CopilotSdk ------------> squad.AgentProvider.Abstractions
  +--> squad.Photino ---------------> squad.Ui.Protocol
                                     squad.Ui.Abstractions
                                     squad.Hosting.Abstractions
                                     squad.Process

squad.Handoffs.Delivery ------------> squad.Handoffs
                                      squad.Configuration

squad.Application ------------------> squad.AgentProvider.Abstractions
  |                                   squad.Ui.Abstractions
  +---------------------------------> squad.Transcripts

squad.Transcripts ------------------> squad.Ui.Abstractions
squad.Ui.Protocol ------------------> squad.Ui.Abstractions
```

No library may reference `squad` or `squad-hq`, and neither technology adapter may be reachable from the application
model, transcript ledger, handoff queue, or agent executable.

## Deliberate non-splits

### Keep the application modules together

`SquadViewModel`, `AgentRoleState`, `AgentRoleSnapshot`, `AgentEventProjector`,
`PendingInteractionRegistry`, and the role-operation leases form distinct internal modules but participate in one
serialized authoritative mutation boundary. Keep them internal to `squad.Application`; separate assemblies would
require public mutation APIs or competing synchronization.

### Keep protocol scheduling and transcript recovery together

`SnapshotPublisher`, `TranscriptAnnouncementJournal`, `PhotinoUiDeliveryCoordinator`, message parsing, command routing,
and recovery records collaborate through one outgoing protocol stream and one synchronization boundary. Put them
behind one `squad.Ui.Protocol` facade rather than creating separate snapshot, journal, and command assemblies.

### Keep contracts independent

Retain `squad.AgentProvider.Abstractions`, `squad.Ui.Abstractions`, and `squad.Hosting.Abstractions` as dependency-free
contract assemblies. Their small size is justified by dependency inversion between independently deployed adapters and
the application or runtime owners.

### Keep executables as composition roots

Do not create `squad.Commands` or `squadHQ.Commands` libraries merely to leave one `Program.cs` in each executable.
Command parsing, console formatting, runtime selection, and object construction are cohesive executable-boundary work.

## Safe implementation plan

Every slice below must be a separately reviewable commit with no intentional behavior change. Start the next slice
only after the current slice builds, its focused acceptance scenarios pass, and clean publishes of both executables
contain every required dependency.

### Slice 0: Restore a trustworthy baseline

**Status: complete (e11bb3529c)**

1. Correct the two stale architecture assertions described above so they represent the current merged handoff
   assembly and pass before production changes.
2. Add a black-box startup scenario proving that roles populated during pre-start preparation are observed by
   `SquadApplication` before role initialization and readiness publication.
3. Record the current startup order around post-lock preparation, sleep inhibition, role initialization, workspace
   preparation, window startup, backend preparation, session registration, handoff recovery, and handoff polling.
4. Keep the existing exact UI protocol, transcript ordering, handoff durability, host ownership, and cleanup scenarios
   as the behavioral baseline.
5. Run the complete acceptance suite and clean publishes; do not proceed from a red baseline.

Slice 0 is accepted when all existing tests are green without changing production behavior and the new startup
characterization fails if role identities are captured before preparation.

### Slice 1: Remove generic process behavior from the Photino boundary

**Status: complete (2fba0199df)**

1. Add asynchronous `RunAsync` and `RunCheckedAsync` behavior to the process module without changing command
   construction, cancellation, output capture, or error text.
2. Add a narrow agent-safe executable locator for `PATH` and `PATHEXT` lookup.
3. Update `WorkspacePreparer` and `Launch` to use those process APIs instead of `squad.Photino.ProcessControl`.
4. Keep detached-process ownership and termination private to the sleep-inhibitor implementation; do not expose
   `System.Diagnostics.Process` lifecycle operations through the agent-reachable process API.
5. Remove the duplicate checked-process implementation after all callers move so there is one error-rendering rule.
6. Amend the agent-safe architecture assertion deliberately to permit only the new safe process surface, while still
   rejecting host lifecycle, UI, delivery-pump, and provider types.

Slice 1 is accepted when `WorkspacePreparer` and `Launch` no longer reference `squad.Photino`, process cancellation
still terminates owned children, command discovery behaves identically on every supported platform, and the `squad`
closure remains explicitly agent-safe.

### Slice 2: Separate durable handoffs from host delivery

**Status: complete (55131f72c9)**

1. Keep `HandoffHeaders`, `HandoffQueue`, `Priority`, `SequenceCounter`, and `Timestamps` in `squad.Handoffs`.
2. Create `squad.Handoffs.Delivery` and move `HandoffDeliveryService`, `InProcessHandoffPoller`, `IHandoffPump`, and
   `IRoleNotifier` into it.
3. Update headquarters and specs references, while ensuring the `squad` executable references only
   `squad.Handoffs`.
4. Preserve all file paths, headers, ordering, collision handling, delivery idempotency, recovery, and notification
   ordering.
5. Assert the final direct references exactly: queue to process only, delivery to queue and configuration only.

This intentionally reverses the packaging part of `0cae493`, but not its behavior or type consolidation: the prior
singular/plural project names obscured the reason for the boundary, while `squad.Handoffs` and
`squad.Handoffs.Delivery` state that the split exists to keep host-only delivery lifecycle out of the agent command
closure.

Slice 2 is accepted when task and batch commands reach only the durable queue module, delivery remains independently
testable, and all handoff, delivery, and recovery scenarios pass.

### Slice 3: Introduce one UI protocol facade in place

1. Introduce a `UiProtocolSession` or equivalently named facade inside the current Photino project before moving any
   files.
2. Move protocol-version ownership, envelope parsing, envelope serialization, protocol-error publication, command
   routing, UI event subscriptions, snapshot scheduling, transcript sequencing, and recovery coordination behind that
   facade.
3. Give the facade a small lifecycle surface equivalent to receiving one serialized message, signaling that sessions
   started, attaching or detaching UI event sources, and disposing delivery work.
4. Make `PhotinoWindowHost` responsible only for creating and closing the native window, forwarding raw browser
   messages, and sending serialized messages through Photino.
5. Repoint `PhotinoUiProtocolSteps` at the facade so protocol behavior is tested without loading Photino.NET.
6. Keep focused tests for snapshot coalescing and announcement journaling; use `InternalsVisibleTo` rather than public
   implementation types if direct test access remains necessary.

Slice 3 is accepted when every protocol scenario passes through the new facade, the protocol version has one owner,
and `PhotinoWindowHost` contains no command validation, transcript synchronization, or envelope construction.

### Slice 4: Extract `squad.Ui.Protocol`

1. Create `squad.Ui.Protocol` and move the facade plus its parser, command handler, delivery coordinator, journal,
   scheduler, synchronization records, and serialization helpers.
2. Rename Photino-prefixed implementation types to technology-neutral names in a separate green commit after the move,
   rather than combining namespace, file, behavior, and assembly changes.
3. Keep one intentional public facade; keep implementation coordinators and records internal unless they are part of a
   supported cross-assembly contract.
4. Move any required `InternalsVisibleTo` declaration from `squad.Photino` to `squad.Ui.Protocol`.
5. Make `squad.Ui.Protocol` reference only `squad.Ui.Abstractions`.
6. Make `squad.Photino` reference the protocol assembly and retain only `PhotinoWindowHost`, `SleepInhibitor`, and
   private native/process helpers.
7. Preserve byte-for-byte equivalent JSON property names, protocol version, message ordering, error text, initial
   synchronization, recovery semantics, and disposal behavior.

Slice 4 is accepted when protocol tests run without Photino.NET, `squad.Ui.Protocol` has exactly one squad dependency,
the Photino package is referenced only by `squad.Photino`, and the existing browser protocol suite remains green
without frontend changes.

### Slice 5: Narrow host startup before extracting workspaces

1. Replace `SquadApplication` dependencies on concrete `Ctx` and `WorkspacePreparer` with one explicitly named startup
   plan that distinguishes context preparation, lazy role lookup, and workspace preparation phases.
2. Evaluate the role provider only after context preparation; do not capture the initially empty production role list
   by value.
3. Preserve the current startup sequence and cancellation points exactly, including the position of sleep inhibition,
   readiness-provider installation, window startup, backend preparation, handoff recovery, and polling.
4. Migrate production and test call sites to the new startup plan before deleting the old constructor.
5. Create `squad.Workspaces` and move `WorkspacePreparer`, `Ctx`, `ProjectLayout`, `RoleConfigRow`, and `SiblingTool`
   after `SquadApplication` no longer mentions them.
6. Keep the first extraction mechanical; rename `Ctx` only in a later green commit if a clearer public name is still
   useful.
7. Restrict `squad.Workspaces` to `squad.Configuration` and `squad.Process`; it must not reference host runtime,
   control, application, UI, Photino, Copilot, or handoff delivery.

Slice 5 is accepted when host runtime construction works with roles discovered during preparation,
`SquadApplication` has no workspace/configuration dependency, reset and continue launch behavior is unchanged, and all
configuration and startup scenarios pass.

### Slice 6: Extract host ownership and control

1. Create `squad.Host.Control` and move `HostLease`, `IHostLease`, `CleanupLease`, `HostControlClient`,
   `HostControlRequest`, and `HostProjectRoot`.
2. Keep console command handlers `Shutdown` and `WaitForAgent` in `squad-hq`; they call the extracted control API and
   remain responsible for argument parsing, output, and exit behavior.
3. Keep request parsing, pipe naming, root normalization, lock ownership, metadata, stale cleanup, and server error
   handling together in the control assembly.
4. Keep `HostControlRequest` and cleanup mechanics internal, granting test access only where required.
5. Restrict the assembly to `squad.Process`; it must not reference sessions, application state, handoffs, UI, or
   technology adapters.

Slice 6 is accepted when main-checkout and linked-worktree discovery, duplicate-host rejection, stale cleanup,
readiness waits, timeouts, malformed requests, and idempotent shutdown remain unchanged.

### Slice 7: Establish a production host-runtime facade

1. Before moving files, introduce one public creation path that owns the shared `SessionRegistry` and
   `SessionRoleNotifier` rather than exposing either implementation.
2. Let that creation path accept a handoff-pump factory shaped like
   `Func<IRoleNotifier, IHandoffPump>` so the notifier, registry, and pump are wired together inside the runtime
   boundary.
3. Keep `SessionRegistry`, `SessionRoleNotifier`, and any registry-sharing constructor internal; do not make them
   public merely because the composition root moves to another assembly.
4. Migrate `Launch` and the common acceptance fixture to the production creation path, then retain direct internal
   construction only for tests that genuinely exercise an internal invariant.
5. Specify `InternalsVisibleTo` independently for `squad.Host.Runtime`; do not rely on the existing friendship declared
   by `squad-hq`.

Slice 7 is accepted when production composition creates exactly one session registry, the same registry drives
readiness and recipient notification, and no public lookup API exposes an unleased session.

### Slice 8: Extract host runtime lifecycle

1. Create `squad.Host.Runtime` and move `SquadApplication`, `SessionRegistry`, `SessionRoleNotifier`, `RunResult`, and
   `ShutdownBeforeReadyException`.
2. Keep process-wide lifecycle coordination in `SquadApplication`; do not perform the generation-lifecycle redesign
   from `020 refactor squad application.md` or `restart button.md` in this move.
3. Preserve startup rollback, terminal-signal precedence, event draining, session disposal order, aggregate cleanup
   failures, handoff recovery, readiness, and host-lease ownership.
4. Restrict direct project references to the five assemblies listed in the target table.
5. Ensure `squad.Host.Runtime` has no reference to workspaces, configuration, UI protocol, Photino, Copilot SDK, or
   either executable.

Slice 8 is accepted when lifecycle, startup, shutdown, backend-failure, event-draining, handoff-notification, and
cleanup-failure scenarios pass without altered ordering or exception semantics.

### Slice 9: Reduce headquarters to composition and enforce the graph

1. Leave `Program`, `Launch`, runtime-mode selection, backend-context construction, `Shutdown`, and `WaitForAgent` in
   `squad-hq`.
2. Remove stale source files, project references, namespace imports, publish artifacts, and friend declarations from
   the old owners.
3. Replace namespace-based architecture assertions with assembly ownership and exact direct-reference assertions where
   namespace names no longer express the physical boundary.
4. Assert that only `squad-hq` references concrete provider and window adapters.
5. Assert that no library references either executable and that the complete project graph is acyclic.
6. Assert that GitHub.Copilot.SDK is referenced only by `squad.CopilotSdk` and Photino.NET only by
   `squad.Photino`.
7. Perform clean builds and clean publishes for every supported runtime identifier and reject stale copies of moved
   assemblies.
8. Update the architecture feature, glossary, and supported architecture documentation to describe the final
   ownership model.

Slice 9 is accepted when `squad-hq` contains only executable-boundary composition and commands, every target assembly
has the exact dependency direction above, and the full acceptance suite and clean publishes are green.

## Per-slice safety rules

- Move one dependency seam at a time; do not combine lifecycle behavior changes with assembly moves.
- Introduce and exercise a narrow API in the old assembly before moving its implementation.
- Update all production and test references in the same slice as a move.
- Keep protocol and persistence compatibility tests black-box and compare observable outputs rather than type names.
- Run the architecture feature in every slice, not only at the end.
- Run the focused feature covering the moved responsibility in the same slice.
- Publish both executables in every slice so missing transitive artifacts fail immediately.
- Use clean output directories when verifying moves so stale DLLs cannot make a broken graph appear valid.
- If an extraction requires a reverse reference, public mutable state, or a pass-through interface, stop and revise the
  boundary instead of introducing the cycle.

## Acceptance criteria

- The target assemblies exist with the responsibilities and direct dependency directions defined above.
- `squad` reaches only `squad.Configuration`, `squad.Handoffs`, and the explicitly agent-safe process surface.
- Host-only handoff delivery types are unreachable from `squad`.
- `squad.Application` retains one authoritative mutation boundary and its role-operation, interaction, and projection
  modules remain internal.
- `squad.Host.Runtime` retains one session registry and one runtime lifecycle authority.
- `squad.Ui.Protocol` owns the protocol version and all envelope validation, serialization, delivery, and recovery.
- `squad.Photino` owns native window and OS integration but no authoritative session, transcript, or protocol state.
- Workspace preparation has no dependency on Photino or host runtime.
- Host control has no dependency on application state, agent sessions, handoffs, UI, or provider adapters.
- No library references `squad`, `squad-hq`, GitHub.Copilot.SDK outside `squad.CopilotSdk`, or Photino.NET outside
  `squad.Photino`.
- No new common/foundation bucket or pass-through assembly is introduced.
- Existing command output, exit codes, configuration validation, handoff durability, transcript ordering, UI messages,
  startup ordering, readiness, shutdown, and cleanup behavior remain unchanged.
- The complete .NET build, acceptance suite, browser protocol compatibility tests, and clean executable publishes pass.

## Relationship to existing issues

This issue builds on the completed `060 eliminate squad core.md` result and preserves its application, transcript, and
contract boundaries. It intentionally restores a separate handoff-delivery boundary after the later `0cae493` merge,
using explicit queue-versus-delivery names and the agent executable closure as the reason for the split.

`020 refactor squad application.md`, `040 refactor session registry.md`, and `restart button.md` remain authoritative
for future lifecycle internals and relaunch behavior. This issue may move their owning types into
`squad.Host.Runtime`, but it must not create a second lifecycle state machine or pre-implement those behavioral
changes.

`050 refactor copilot sdk agent session.md` remains internal to `squad.CopilotSdk` and does not alter this assembly
graph.
