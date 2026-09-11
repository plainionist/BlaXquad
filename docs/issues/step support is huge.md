---
title: Organize and simplify acceptance-test support
priority: 10
---

# Organize and simplify acceptance-test support

## Problem

`src/squad.Specs/Support` is a flat namespace containing 35 files with several unrelated responsibilities:

- scenario orchestration and workspace setup;
- child-process execution and diagnostics;
- the headless UI protocol client and transcript observations;
- fake and echo agent-provider implementations;
- the fake-provider control transport;
- handoff and task mailbox fixtures; and
- provider-selection fixtures.

This makes ownership hard to see and turns `Support` into a default destination for new test infrastructure. Several
large types are also likely to contain more than one independent concern, while small records, protocol handlers,
fixtures, and exceptions sit beside them without any visible grouping.

The directory currently defines three custom exception types. Their existence is not automatically a defect:
callers distinguish some of them and the timeout exceptions preserve useful failure diagnostics. Each exception
still needs an explicit reason to exist; a custom type that only renames a built-in exception or formats a message
adds maintenance cost without adding behavior.

This issue concerns the implementation structure of the acceptance-test support. The feature language and binding
organization were handled by the completed "Build a reusable Gherkin step language" work and remain out of scope here.

## Goal

Make the acceptance-test support easy to navigate, cohesive, and no larger than its responsibilities require.
Organize support by the external boundary it drives, remove dead or redundant helpers, and retain custom types only
when they provide distinct behavior, handling, or diagnostics.

The result must remain a behavior-preserving refactoring of the black-box suite. It must not change product behavior,
weaken scenario coverage, introduce production APIs for tests, or create production-shaped test assemblies.

## Scope

- Classify every file in `src/squad.Specs/Support` by one owning responsibility.
- Introduce responsibility-based folders and matching namespaces.
- Review the cohesion of the largest support types and extract only independently owned state, lifecycle, I/O,
	persistence, or protocol concerns.
- Review custom exceptions, accessibility, duplicate helpers, and unused support code from their call sites.
- Preserve one scenario-scoped owner for startup, teardown, and shared scenario state.

## Target structure

Use a small set of responsibility folders. The exact names may be adjusted during the inventory, but every folder
must have a clear owner and must not merely mirror production assemblies.

```text
Support/
	Scenarios/          scenario facade, role/command adapters, workspace lifecycle
	Processes/          child-process execution, command results, process diagnostics
	Ui/                 headless UI client and UI/transcript observations
	Agents/             fake/echo providers and provider-selection fixtures
		Control/          fake-provider control transport, protocol, handlers, and waits
	Mailboxes/          handoff and task setup/observation
```

Namespaces must follow these folders. Keep one top-level type per source file. A type used by only one owner may be
placed beside that owner instead of creating a one-file category.

This structure is an ownership map, not a mandate to add a layer around every file. Do not introduce pass-through
wrappers or interfaces with only one implementation merely to make the tree look uniform.

## Design constraints

### Keep the black-box boundary

All support remains in `squad.Specs` and continues to drive the real executables, protocols, filesystem, Git, and
handoff storage. Only the external agent provider remains fake. Do not add test hooks, callbacks, alternate product
behavior, `InternalsVisibleTo`, or new production abstractions for this cleanup.

### Split by responsibility, not size

File length identifies review candidates, not automatic extraction points. A split is justified when a concern has
its own state, lifecycle, I/O boundary, or reason to change. Keep related state and synchronization together even if
the resulting owner is substantial.

`BackendScenario` remains the scenario composition root. Extracted collaborators may own cohesive work, but must not
duplicate startup, teardown, mutable scenario state, or protocol reconciliation. Step definitions should continue to
use the scenario facade instead of assembling support objects independently.

### Keep failure diagnostics actionable

Review `FakeProviderControlProtocolException`, `FakeProviderControlTimeoutException`, and
`HeadlessUiWaitTimeoutException` from their throw, catch, and assertion sites.

Retain a custom exception only when at least one of these is true:

- a caller must distinguish that failure category;
- it carries structured diagnostic state or behavior not supplied by the base exception; or
- the type forms a deliberate test-support contract that makes a meaningful assertion clearer.

Otherwise use the appropriate built-in exception and a shared diagnostic formatter where needed. Removing a custom
exception must not discard captured process, protocol, UI, or provider state from the failure message.

### Remove proven redundancy

Use reference searches before deleting or merging support. Remove dead fixtures, handlers, records, aliases, and
duplicate formatting or waiting logic only after confirming that no scenario depends on their behavior. Preserve
bounded waits, deterministic acknowledgements, cancellation, cleanup, and failure diagnostics.

## Verified inventory

The current tree contains 35 C# files. Every existing file has the following owner and disposition:

| Owner | Existing files | Disposition |
| --- | --- | --- |
| `Scenarios` | `BackendScenario.cs`, `BackendScenarioAgent.cs`, `BackendScenarioCommand.cs`, `ScenarioWorkspace.cs` | Keep the semantic scenario facade, role/command adapters, and temporary-workspace owner together. Extract only the low-level process runner from `ScenarioWorkspace`. |
| `Processes` | `CancellableChildProcess.cs`, `CommandResult.cs`, `ProcessDiagnostics.cs` | Keep isolated cancellation, captured command results, and process diagnostics together. Add the extracted scenario-owned child-process runner here. |
| `Ui` | `ArchivedTranscriptEntryObservation.cs`, `HeadlessUiClient.cs`, `HeadlessUiWaitTimeoutException.cs`, `TranscriptEntryObservation.cs`, `TranscriptPageObservation.cs`, `TranscriptSynchronizationObservation.cs`, `TranscriptUpdateObservation.cs` | Keep semantic UI operations and decoded observations together. Extract stream capture and transcript parsing/reconciliation; remove the custom timeout type. |
| `Agents` | `EchoAgentProviderFactory.cs`, `FakeAgentBackend.cs`, `FakeAgentProviderFactory.cs`, `FakeAgentRuntime.cs`, `FakeAgentSession.cs`, `IncompatibleProviderFixture.cs`, `TestAgentEventStream.cs`, `ThrowingProviderFixtureFactory.cs`, `ValidProviderFixtureFactory.cs` | Keep provider fixtures and provider-side lifecycle together. Remove the redundant echo provider; retain the fake session and event stream as cohesive owners. |
| `Agents/Control` | `ControlPipeDuplex.cs`, `FakeProviderControlClient.cs`, `FakeProviderControlProtocolException.cs`, `FakeProviderControlServer.cs`, `FakeProviderControlTimeoutException.cs`, `FakeProviderEmitHandler.cs`, `FakeProviderFailBackendHandler.cs`, `FakeProviderReplyHandler.cs` | Keep the private fake-provider transport, protocol, handlers, and observations together. Extract the observation journal and remove both custom exception types. |
| `Mailboxes` | `HandoffDraftWriter.cs`, `HandoffMailboxObserver.cs`, `TaskMailboxFixture.cs`, `TaskMailboxObserver.cs` | Keep durable handoff/task setup and observation together. Move the `QueuedHandoff` top-level record out of `HandoffMailboxObserver.cs` into its own file. |

`EchoAgentProviderFactory.cs` currently contains four top-level types and `HandoffMailboxObserver.cs` contains two.
Removing the obsolete echo fixture eliminates the first violation; extracting `QueuedHandoff.cs` fixes the second.

## Architecture decisions

- Use exactly these namespaces: `squad.Specs.Support.Scenarios`, `.Processes`, `.Ui`, `.Agents`,
  `.Agents.Control`, and `.Mailboxes`.
- Keep `BackendScenario` as the semantic composition root and normal/emergency teardown owner. It may coordinate
  workspace, process, UI, and fake-provider collaborators, but step definitions must not construct default support
  graphs themselves. Explicit concurrent projects and replacement launches remain children created and tracked by
  that root.
- Extract a scenario-owned process runner from `ScenarioWorkspace`; it owns `ProcessStartInfo`, redirected-stream
  capture, tracked child processes, and bounded process cleanup. `ScenarioWorkspace` retains temporary-directory,
  Git-project, worktree, tool-selection, and filesystem cleanup semantics.
- Extract the headless UI's redirected-stream capture/journal from `HeadlessUiClient`, and extract transcript message
  parsing/reconciliation into a protocol-focused helper. `HeadlessUiClient` remains the semantic UI client; do not
  add a generic UI abstraction or a pass-through interface.
- Extract the fake-control observation state, bounded waits, and diagnostic rendering from
  `FakeProviderControlServer`. The server remains the semantic command surface and owns the authenticated pipe
  lifecycle through `ControlPipeDuplex`.
- Keep `CancellableChildProcess` intact: its private Windows and Unix implementations serve one cohesive
  isolated-child-process responsibility. Keep `FakeAgentSession` intact: its state and deterministic controls all
  govern one `IAgentSession` lifecycle. Size alone does not justify another split.
- Remove `FakeProviderControlProtocolException`, `FakeProviderControlTimeoutException`, and
  `HeadlessUiWaitTimeoutException`. No caller catches or asserts any concrete one of these types, and none carries
  structured state. Use `InvalidOperationException` for rejected protocol operations and `TimeoutException` for
  bounded waits while preserving the current process, UI, protocol, and provider diagnostic text.
- Remove `EchoAgentProviderFactory` and its backend/runtime/session types. Its only two setup call sites need
  readiness, which `FakeAgentProviderFactory` already provides; controlled auto-echo already exists for scenarios
  that actually need a reply.
- Remove the proven-unused `ScenarioWorkspace.StartHeadlessUiClient`, `ScenarioWorkspace.StartTool`, and
  `ScenarioWorkspace.ConfigureProject(params string[])` members. Retain the named control-handler delegates and
  `TestAgentEventStream`: they have live callers and express the private protocol/session contracts without adding
  interfaces or layers.
- Make support types and members internal unless Reqnroll must construct them or the separately launched provider
  loader must reflect over them. The provider factory fixture types selected through `--provider` remain public;
  public accessibility is not otherwise a convenience default.

## Implementation slices

Only one slice may be in progress at a time. Each slice includes its moves, namespace/import changes, owner-specific
cleanup, focused acceptance scenarios, and a complete `squad.Specs` run before review.

### Slice 1 - Mailbox support has one durable-state owner [done]

**Outcome:** Handoff and task setup/observation retain their exact on-disk behavior while all mailbox support is under
`Support/Mailboxes` with one top-level type per file.

- Move the four mailbox files to `Support/Mailboxes` and change their namespace to
  `squad.Specs.Support.Mailboxes`.
- Extract `QueuedHandoff` unchanged into `QueuedHandoff.cs`.
- Replace `new HandoffDraftWriter`, `new HandoffMailboxObserver`, `new TaskMailboxFixture`, and
  `new TaskMailboxObserver` in bindings with constructor-injected, scenario-scoped instances sharing the existing
  `ScenarioWorkspace`. Do not move parsing, file naming, seeding, or state-transition knowledge into step
  definitions.
- Reduce mailbox types and members to internal accessibility where Reqnroll resolution permits it.
- Preserve stable file ordering, byte-for-byte recovery snapshots, fan-out headers, archive-collision setup, and
  role-worktree scoping.

Focused acceptance: `Handoffs.feature`, `Delivery.feature`, `TaskQueue.feature`, `BatchQueue.feature`, and
`Recovery.feature`.

### Slice 2 - Process execution and cleanup have one owner [done]

**Outcome:** Real CLI processes still run, capture output, receive isolated cancellation, and clean up with the same
bounded diagnostics while low-level process mechanics live under `Support/Processes`.

- Move `CancellableChildProcess`, `CommandResult`, and `ProcessDiagnostics` to `Support/Processes` and update all
  imports.
- Extract an internal `ScenarioProcessRunner` from `ScenarioWorkspace`. It owns start/run mechanics, redirected
  stream capture, environment construction, output normalization, process tracking, and bounded child termination.
  `ScenarioWorkspace` continues to choose published tools and working directories, apply Git identity, record the
  latest command result, and dispose the runner before deleting its temporary tree.
- Delete the unused `StartHeadlessUiClient`, `StartTool`, and parameter-array `ConfigureProject` overload after
  reference verification.
- Keep the platform-specific process-group code private inside `CancellableChildProcess`; do not introduce a process
  interface or fake process.

Focused acceptance: `Context.feature`, `AgentProviderSelection.feature`, `UiSelection.feature`,
`HeadquartersTermination.feature`, and `HeadquartersCleanupDiagnostics.feature`.

### Slice 3 - Headless UI transport and protocol observations are separate

**Outcome:** The real stdio UI protocol retains framing, ordering, transcript reconciliation, and timeout diagnostics
while stream I/O and transcript interpretation have explicit owners under `Support/Ui`.

- Move the headless client and all six UI/transcript observation files to `Support/Ui`.
- Extract an internal headless UI transport that owns standard-input writes, concurrent stdout/stderr draining,
  synchronized snapshots, input closure, and process/output diagnostics.
- Extract transcript envelope matching, decoding, paging, and synchronization/update reconciliation into one
  protocol-focused helper. Keep role state, usage, tool, and pending-interaction predicates in the semantic client
  unless they acquire independent state or lifecycle.
- Remove `HeadlessUiWaitTimeoutException`; throw `TimeoutException` with the existing description and full combined
  process/UI/provider diagnostic block.
- Keep raw JSON and stream access behind `HeadlessUiClient`; do not change protocol version, commands, wait ordering,
  skip semantics, or observation records.

Focused acceptance: `StdioUiProtocol.feature`, `StdioUiProtocolMultiRole.feature`, `UiProtocolValidation.feature`,
all `Transcript*.feature` files, and the dashboard interaction scenarios.

### Slice 4 - Provider fixtures have one lifecycle owner

**Outcome:** Provider-selection and fake-session behavior remain unchanged while provider fixtures live under
`Support/Agents` and the duplicate echo stack is gone.

- Move the fake backend/factory/runtime/session, provider-selection fixtures, and `TestAgentEventStream` to
  `Support/Agents`.
- Replace the two `EchoAgentProviderFactory` launch sites with `FakeAgentProviderFactory`, then delete
  `EchoAgentProviderFactory.cs` and its three additional top-level types. Do not enable a control pipe where those
  readiness-only scenarios do not need one.
- Keep `FakeAgentSession` as the owner of one session's event stream, pending reply/abort/disposal state, readiness
  generation, and deterministic controls. Do not split event publication from the state it mutates.
- Keep only the four externally reflected provider fixture types public:
  `FakeAgentProviderFactory`, `ValidProviderFixtureFactory`, `ThrowingProviderFixtureFactory`, and
  `IncompatibleProviderFixture`.

Focused acceptance: `AgentProviderSelection.feature`, `ProviderPackaging.feature`, `HeadquartersLifecycle.feature`,
and the host-ownership replacement scenarios.

### Slice 5 - Fake-provider control transport owns protocol state

**Outcome:** Concurrent fake-provider commands and observations retain deterministic acknowledgements, bounded waits,
and complete diagnostics while transport and observation state are explicit owners under `Support/Agents/Control`.

- Move the duplex, client, server, handlers, and current control exceptions to `Support/Agents/Control`.
- Extract an internal observation journal from `FakeProviderControlServer`. It owns the synchronized lifecycle list,
  active session ids, latest prompts and generic observations, observation counts, protocol errors, bounded
  observation waits, undisposed-session reporting, and diagnostic rendering.
- Keep `ControlPipeDuplex` as the single-reader/correlated-reply transport and keep the server as the authenticated
  protocol/semantic command owner; do not add an interface around either one.
- Remove both custom control exceptions. Preserve immediate `InvalidOperationException` failures for missing/rejected
  sessions and `TimeoutException` failures with the full control plus UI/process diagnostic text.
- Preserve authentication, protocol-version validation, correlation ids, acknowledgement-before-backend-failure
  ordering, cancellation, and disposal behavior.

Focused acceptance: `StdioTransportConcurrentDispatchAndShutdown.feature`,
`PromptIsolationAndReadiness.feature`, the interaction publication/cancellation scenarios, terminal-provider failure
scenarios, and cleanup-diagnostic scenarios.

### Slice 6 - Scenario composition owns all scenario lifetimes

**Outcome:** Every default, replacement, and explicitly concurrent backend scenario remains black-box and is cleaned
up by one scenario-scoped composition root while the final support tree and namespaces communicate ownership.

- Move `BackendScenario`, `BackendScenarioAgent`, `BackendScenarioCommand`, and `ScenarioWorkspace` to
  `Support/Scenarios`; update binding imports without changing feature wording.
- Make `BackendScenario` create, track, and dispose replacement launches and explicit independent-project children.
  Route recovery restarts and host-coexistence setup through that API instead of constructing
  `BackendScenario`/`ScenarioWorkspace` directly in bindings. Preserve the intentional ability to address each named
  child independently.
- Keep normal shutdown explicit and emergency termination bounded. Dispose child scenarios before their workspaces
  and dispose the process runner before workspace deletion; no binding may become a second teardown owner.
- Complete the accessibility/reference sweep owner by owner. Do not add forwarding interfaces, production hooks, or
  production-facing APIs.
- Add the durable six-folder ownership map to `docs/Manual/test-strategy.md`; keep detailed behavior in the existing
  feature files.

Focused acceptance: `HostCoexistence.feature`, `Recovery.feature`, all headquarters lifecycle/termination features,
followed by the complete `squad.Specs` suite.

## Acceptance criteria

- Every file under `src/squad.Specs/Support` belongs to a named responsibility folder, and its namespace matches its
	folder.
- The resulting folders separate scenario orchestration, process support, UI protocol support, agent-provider and
	control support, and mailbox support without mirroring the production project graph.
- `BackendScenario` remains the single scenario-scoped composition root and teardown owner; support is not assembled
	independently by individual step-definition classes.
- Every source file contains one top-level type, and types and members are public only when an assembly or framework
	boundary requires it.
- Each retained custom exception has a concrete caller or diagnostic reason that a built-in exception cannot express
	as clearly. Exceptions that only rename a built-in type or format a fixed message are removed.
- Timeout and protocol failures still report the relevant process output, protocol observations, UI state, and
	provider-control diagnostics needed to investigate a failed scenario.
- Large support types have been reviewed for independent concerns. Any extraction owns real state, lifecycle, I/O,
	or protocol behavior; no pass-through layers or one-implementation interfaces are introduced.
- Unused support and proven duplicate parsing, waiting, or diagnostic code are removed without changing deterministic
	synchronization or cleanup behavior.
- Feature wording and Gherkin binding vocabulary are unchanged by this issue except for namespace/import updates
	required by file moves.
- No production assembly or public product API changes solely to accommodate the test refactoring.
- The complete `squad.Specs` suite passes after the reorganization.

## Non-goals

- Redesigning the Gherkin vocabulary or reorganizing step definitions; that belongs to the reusable-step-language
	issue.
- Splitting `squad.Specs` into production-aligned test assemblies.
- Replacing real Git, filesystem, process, UI protocol, host-control, handoff, or task behavior with mocks.
- Changing product behavior, supported protocols, scenario semantics, or timing guarantees.
- Enforcing arbitrary file-size or line-count limits.

## Slice 1 review (2cb3b53c7c) — accepted

**Status: complete (2cb3b53c7c).** Mailbox support is under `Support/Mailboxes` with matching namespace and one type per file (`QueuedHandoff` extracted). Bindings use constructor-injected, scenario-scoped mailbox instances that share `ScenarioWorkspace` instead of calling `new`. Members are internal; types stay public for Reqnroll resolution. Stable file ordering, byte-for-byte recovery snapshots, fan-out headers, archive-collision setup, and role-worktree scoping are unchanged.

## Slice 2 review (9620f59c05) — accepted

**Status: complete (9620f59c05).** Process types live under `Support/Processes`. Internal `ScenarioProcessRunner` owns start/run, stream capture, environment construction, normalization, tracking, and bounded termination. `ScenarioWorkspace` still chooses tools and working directories, applies Git identity, records `LastResult`, and disposes the runner before deleting the temp tree. Unused `StartHeadlessUiClient`, `StartTool`, and the params `ConfigureProject` overload are gone. Platform process-group code stays private in `CancellableChildProcess`; no process interface or fake process was added.
