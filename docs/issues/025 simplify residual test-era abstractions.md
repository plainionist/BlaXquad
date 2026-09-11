---
title: Simplify residual test-era abstractions
priority: 25
---

# Simplify residual test-era abstractions

## Problem

The backend specifications now exercise published processes and protocols. They no longer construct
`SquadApplication`, `SquadViewModel`, workspace preparation, handoff delivery, or runtime lifecycle objects directly.
Issue 022 removed most of the public test surface, but several production designs still preserve variability that
was introduced for the former white-box suite.

The history makes that origin explicit:

- `959a828` introduced `SquadStartupPlan` so direct `SquadApplication` specifications could replace preparation
  phases, gate them, and inject failures independently.
- `326cb95` introduced `StandaloneSessionAdmission` specifically so the many specifications that constructed a bare
  `SquadViewModel` would continue to work without the production lifecycle authority.
- `967d9fd` introduced `SquadApplication.Create` and its handoff-pump factory so production and direct application
  fixtures could construct different pumps around an internal registry/notifier pair.
- `aad2f04` removed the last specifications that directly constructed `InProcessHandoffPoller`, supplied a recording
  handoff log callback, or varied delivery collaborators. Issue 022 subsequently removed `IHandoffPump`, but left the
  one-shot concrete-poller factory in place.
- `7d9cba7` removed the broad `IRuntimeModeFactory` and its deferred preparation behavior. `RuntimeMode` now only
  returns the provider factory it was given together with the two platform objects always constructed by `Launch`.

Current `squad.Specs` references product code only through `squad.AgentProvider.Abstractions`; no current step or
support type constructs the objects above. The remaining indirection therefore has to justify itself from production
callers and module ownership, not from tests that no longer exist.

This issue is not a request to remove every interface with one implementation. A narrow interface can still be the
right dependency boundary even when it is not a strategy. The target is indirection that expresses nonexistent
choice, duplicate authority, deferred mutable state, or a test-only capability.

## Goal

Make headquarters composition describe its one real production path directly. Remove callback bags, one-call
factories, fallback implementations, duplicate session projections, and fake-only provider capabilities while
preserving startup cancellation, lifecycle failure precedence, command admission, readiness, handoff durability,
and cleanup behavior through the existing process boundaries.

Do not replace a removed delegate with a one-method interface or a renamed factory unless there are at least two real
production behaviors or the interface is required to preserve a deliberate module dependency direction.

## Designs to simplify

### Replace the callback-based startup plan with one concrete preparation owner

`SquadStartupPlan` stores five behavior delegates plus `continueLaunch`. `SquadStartupPlanFactory.ForWorkspace` has
one caller and binds every delegate to the same mutable `Ctx` and `WorkspacePreparer`. The resulting methods only
invoke those delegates. Lazy role discovery is necessary solely because the plan and handoff poller are constructed
before `WorkspacePreparer.Parse` replaces `Ctx.Roles`.

This structure preserves the old test matrix in which each preparation phase could be replaced independently. It
does not represent production variability now:

- configuration is always parsed by `WorkspacePreparer`;
- workspace, worktree, and handoff-directory preparation always use that same instance and context;
- `continueLaunch` is fixed for the launch;
- role configuration is populated once and never reloaded; and
- `SquadStartupPlanFactory` and `SquadStartupPlan` have one production construction path.

Replace the callback bag with one concrete owner of launch preparation and one immutable prepared result. The result
should contain the backend context, application role identities, handoff role descriptors, and paths needed by the
runtime after parsing. Materialize those values once rather than retaining closures over mutable `Ctx` state.

Delete `SquadStartupPlanFactory`. Delete `SquadStartupPlan` unless it becomes a data-only prepared value; it must not
survive as the same callback bag behind a new name. Put the concrete preparation owner in the module that owns the
work. If the current assembly split prevents a direct dependency, move the orchestration boundary rather than
recreating one interface or delegate per preparation phase.

Preparation must remain inside the startup task observed by `SquadApplication.RunAsync`, or an equivalent owner, so
host-control shutdown can cancel an in-progress Git/configuration/worktree operation and cleanup still observes the
startup result before disposing owned resources. Preserve observable ordering constraints, not the historical
ability to substitute each step.

### Inline the residual runtime-mode wrapper

`Launch.Create` is called once. It accepts an already selected `IAgentProviderFactory`, returns that same instance,
always creates `SleepInhibitor`, and chooses between the two real `IWindowHost` implementations. `RuntimeMode` only
bundles those three values.

This is the shell left after `IRuntimeModeFactory` and its independent preparation behavior were removed. Inline the
composition in `RunMain` and delete `RuntimeMode` and the one-use `Create` helper. Keep the `UiMode` branch and
`IWindowHost`: stdio and Photino are two genuine production transports. Keep `ISleepInhibitor` as the platform
boundary consumed by the runtime; the redundant part is the wrapper, not those contracts.

### Replace `WorkspacePreparer`'s failure callback with typed failures

`WorkspacePreparer` accepts `Action<string> fail`, and production always supplies the local `Launch.Fail` method.
Former direct tests commonly supplied `_ => { }`. The implementation implicitly requires the action to throw: after
reporting a missing configuration, missing constitution, invalid helper, or unsafe shared path, execution otherwise
continues with invalid state. `Action<string>` cannot express that contract.

Remove the callback and make `WorkspacePreparer` throw a workspace/configuration exception containing an unformatted
diagnostic. Translate that exception to `CliExitException` and ANSI presentation once at the `squad-hq` command
boundary. Reuse an existing suitable exception where one accurately represents the failure; otherwise add one
cohesive workspace exception rather than one exception type per branch.

This also removes the duplicate `new WorkspacePreparer(Fail)` used only by `Launch.PrepareContext`; one preparer owns
the complete launch preparation.

### Make handoff-poller configuration concrete

The handoff path currently composes three layers of artificial variability:

- `SquadApplication.Create` accepts `Func<IRoleNotifier, InProcessHandoffPoller>` even though there is one caller and
  one concrete poller;
- `InProcessHandoffPoller` accepts `Func<IReadOnlyList<RoleRow>>` even though roles are assigned once during parsing
  and never reloaded; and
- `InProcessHandoffPoller` and `HandoffDeliveryService` accept `Action<string[]>` even though production always
  appends the same timestamped line to the configured handoff log.

These seams were useful when direct delivery specifications supplied fixed roles, a recording notifier, and an
in-memory log. Those specifications now drive the real filesystem poller and observe behavior at the process,
provider, and mailbox boundaries.

Construct the concrete poller after launch preparation has materialized immutable role descriptors. Pass those
roles directly. Give delivery a concrete log destination or return a typed delivery outcome to a concrete owner;
do not retain an arbitrary logging action solely to reproduce the old recording test. Remove
`handoffPumpFactory` and let the production composition owner create the poller around the one
`SessionRoleNotifier`.

Keep `IRoleNotifier`. It is the intentional cross-module direction from filesystem delivery to active role sessions;
removing it would make `squad.Handoffs.Delivery` depend on host-runtime/application internals. Preserve recovery,
per-file failure archival, terminal poll-loop failure reporting, and live cancellation.

### Establish one session catalog and one admission authority

The current lifecycle records each provider session three times:

- `SessionRegistry` delegates role lookup to `SessionCatalog`;
- `SquadViewModel` keeps a second dictionary for readiness, event validation, and interaction cancellation; and
- `SessionGeneration` keeps a third dictionary that is only populated and cleared and is never read.

`StandaloneSessionAdmission` duplicates the registry's accepting/session lookup behavior and is installed by
default, then replaced through `SquadViewModel.UseAdmission` on the only production path. Commit `326cb95` documents
that fallback as compatibility for bare-ViewModel specifications. Those specifications no longer exist.

There is more nominal state than effective behavior as well: `SessionLease.Generation` is never read, and
`SessionRegistry.myGeneration` only supplies that unused value. Atomic command admission comes from checking the
lifecycle state and selecting the active session under the same synchronization boundary, not from returning an
unused integer in a wrapper.

Choose one owner for active sessions and command admission and route ViewModel commands, readiness, pending
interaction cancellation, and handoff notification through it. Prefer the existing authoritative application
command boundary if that lets the duplicate catalogs disappear. Then:

- remove `StandaloneSessionAdmission` and replaceable `UseAdmission` behavior;
- remove the unread `SessionGeneration` dictionary;
- remove the unused generation identity and `SessionLease`, returning the admitted session directly if no actual
  lease lifetime remains;
- fold `SessionCatalog` into its sole owner unless it gains an independent responsibility; and
- remove `ISessionAdmission` from `squad.AgentProvider.Abstractions`, which should contain provider SPI rather than
  host/application lifecycle plumbing. Eliminate it or relocate a genuinely necessary narrow boundary to its owning
  module.

Do not regress the atomic admission invariant. Once stopping begins, no new UI or handoff operation may acquire a
session, while work admitted before that point must reach its defined cancellation/drain outcome before provider
session disposal.

### Replace internal lifecycle relay delegates with direct collaborators

Two runtime methods accept callbacks for operations that have exactly one production target:

- `SquadRuntimeController.StartAsync` receives `myWindowHost.SessionsStartedAsync`; and
- `SessionGeneration.StartAsync` receives `SquadRuntimeController.RegisterSessionAsync`.

These callbacks do not select behavior. They relay calls between objects already assembled for the same lifecycle.
Give the coordinator the concrete `IWindowHost` collaborator it coordinates, or keep the call in
`SquadApplication` if that owner should control the ordering. Give `SessionGeneration` the single session authority
it registers with instead of passing a callback through every start. Referencing a collaborator does not transfer
its disposal ownership; keep cleanup in `SquadApplication`.

Retain `IAgentRuntime.StartAsync(Func<IAgentSession, Task>, ...)`: provider runtimes genuinely create sessions and
must publish them across the provider SPI. The target is the additional host-internal relay callback, not the
provider callback. If `SquadLifecycleTransition` remains after admission consolidation, replace its arbitrary
commit/fail actions with direct state owned by the lifecycle authority.

### Remove the fake-only readiness capability from the provider SPI

`IAgentReadinessProbe` has one implementation: `FakeAgentSession` in `squad.Specs`. No production provider
implements it. Its nominal probe, `ObserveReadinessAsync`, always returns `null`; the other two members maintain a
generation used only by fake-emitted `AgentReadinessEvent` values. `AgentReadinessEvent` itself has no production
emitter.

This is test control represented as a product capability. Remove `IAgentReadinessProbe` and, unless a real provider
use is identified during implementation, remove `AgentReadinessEvent`. Drive readiness from the normal provider
lifecycle events already used in production: session started, idle after completed work, stopped, and failed. Keep
stale-event suppression in the host-owned session/lifecycle state rather than asking an optional provider capability
to validate a test-generated counter.

The user-facing `squad-hq wait-for-agent` behavior remains. `HostLease` still needs the production readiness source
from application state; this section removes the provider-type conditional inside that source, not host-control
readiness reporting. Recast fake-provider commands and scenarios around idle/busy/session events without adding a
replacement test hook to product code.

## Intentional boundaries to retain

Do not remove these merely because an implementation count is currently small:

- `IAgentProviderFactory`, `IAgentBackend`, `IAgentRuntime`, and `IAgentSession` are the supported provider SPI and
  have real production/test-provider variation across the process-loading boundary.
- `IAgentBackendFailureSource` is an optional provider capability with observable backend-wide failure semantics.
- `IWindowHost` has Photino and stdio implementations with genuinely different transports.
- `ISleepInhibitor` keeps platform process management out of the provider-neutral runtime lifecycle module.
- `ISquadUi` and `ITranscriptUi` keep authoritative application state independent of JSON/native presentation
  adapters.
- `IRoleNotifier` preserves the delivery-to-runtime dependency direction.
- UI serialized-message callbacks and provider session-start callbacks connect real asynchronous producers to
  multiple consumers; they are not test substitution points.
- `HostLease.SetAgentReadinessProvider` represents deliberately late product wiring: the lease must accept early
  shutdown/readiness requests before provider sessions are available.

## Implementation plan

Exactly one slice is active at a time. Each slice includes its production changes, black-box acceptance coverage,
obsolete test-support cleanup, and directly affected manual updates.

### Slice 1 - Workspace failures terminate at the command boundary [done]

**Outcome:** Every invalid workspace or configuration stops launch with the existing operator-facing diagnostic,
without relying on a callback whose caller must throw.

- Add one `WorkspacePreparationException` in `squad.Workspaces` carrying an unformatted diagnostic. Make
  `WorkspacePreparer` parameterless and throw that exception for missing configuration or constitution files,
  invalid squad configuration, a missing helper, and an unsafe shared worktree path. Preserve the original
  configuration diagnostic and inner exception where applicable.
- Use one `WorkspacePreparer` instance for the current startup path. Remove `Launch.Fail` from workspace preparation
  and remove the duplicate preparer used by context parsing.
- At the `squad-hq launch` command boundary, translate only `WorkspacePreparationException` to
  `CliExitException`, adding the ANSI `Error:` presentation exactly once. Do not let workspace failures acquire the
  generic `Provider startup failed` label, and do not catch unrelated failures as workspace errors.
- Protect the observable diagnostics through the published `squad-hq` process for missing and malformed
  configuration, a missing constitution, a missing helper, and a non-empty shared-path collision. Reuse existing
  scenario support and real filesystem/process behavior; add no failure callback, product test mode, or injectable
  workspace collaborator.

**Acceptance:** `WorkspacePreparer` has no failure callback and cannot continue with invalid state. Each covered
launch exits non-zero with its specific diagnostic, no duplicate `Error:` prefix, no provider-startup
misclassification, and no raw unhandled-exception output. The healthy headquarters lifecycle still reaches
readiness.

**Status: complete (413d3eb049).** `WorkspacePreparer` is parameterless and throws one `WorkspacePreparationException`
with an unformatted diagnostic (preserving `SquadConfigurationException` message and inner exception). Launch uses
one preparer, translates only this exception at the `squad-hq launch` boundary with a single ANSI `Error:` prefix,
and keeps other failures off the workspace path. `HeadquartersWorkspaceFailures.feature` covers missing/malformed
configuration, missing constitution, missing helper, and non-empty shared-path collision through the published
process.

### Slice 2 - One immutable preparation result composes the runtime and delivery [done]

**Outcome:** Headquarters performs one cancellable concrete preparation pipeline and constructs every
post-preparation collaborator from the immutable values it produces.

- Put a concrete launch-preparation owner and an immutable prepared-launch value in `squad.Workspaces`. The owner
  encapsulates the mutable `Ctx` used while discovering the repository and, under the startup cancellation token,
  performs Git initialization/excludes, configuration parsing, workspace and worktree preparation, shared-path
  setup, and handoff-directory creation. The result materializes the `AgentBackendContext`, ordered application role
  names, leader, fixed handoff `RoleRow` values, and handoff log path; no result member may expose `Ctx`, a lazy
  sequence, or a closure over it.
- Let `SquadApplication` depend on that concrete owner and await it inside the startup task before creating the
  backend or any collaborator that needs prepared data. A shutdown received during preparation must cancel the same
  task, and cleanup must observe its terminal result before disposing resources.
- After preparation, construct the one `SessionRoleNotifier` and one `InProcessHandoffPoller` directly from the
  prepared roles and log path. Make delivery append its timestamped diagnostics to that concrete destination.
  Remove `SquadApplication.Create`'s poller factory, the poller's role-provider function, and both arbitrary logging
  actions. Because the poller does not exist before preparation, observe its failure after startup in the same
  race-safe manner as the late-created backend failure and dispose it only when construction completed.
- Compose the selected `IWindowHost` and `SleepInhibitor` directly in `Launch.RunMain`. Delete `RuntimeMode`,
  `Launch.Create`, `SquadStartupPlanFactory`, and the behavior-carrying `SquadStartupPlan`; do not replace any of
  them with another factory, phase interface, or delegate bag.
- Update the workspace/runtime module descriptions if ownership or dependencies change.

**Acceptance:** Healthy and continued launches retain role order, leader, initial instructions, UI-before-session
ordering, recovery-before-polling, and cancellation. Early shutdown, partial provider startup, cleanup failure
precedence, and terminal backend or poller failures retain their diagnostics and resource release. Handoff fan-out,
notification failure, recovery, and duplicate prevention remain durable. The focused gates are
`HeadquartersLifecycle`, `HeadquartersEarlyShutdown`, `HeadquartersPartialStartupFailure`,
`HeadquartersCleanupDiagnostics`, `HeadquartersTerminalProviderFailure`, `HeadquartersHandoffPumpFailure`,
`Delivery`, and `Recovery`.

**Status: complete (3eb8a1752e).** `LaunchPreparer` owns mutable `Ctx` and returns a materialized `PreparedLaunch`.
`SquadApplication` awaits that pipeline before backend or poller construction, observes late pump failure like the
backend, and disposes the poller only when it was created. Delivery logs to `HandoffDeliveryLog`. `RuntimeMode`,
`Launch.Create`, `SquadStartupPlanFactory`, and `SquadStartupPlan` are gone. Architecture records the lifecycle to
workspace-manager dependency.

### Slice 3 - Production lifecycle events are the only readiness source

**Outcome:** `wait-for-agent` derives readiness from the same session, prompt, idle, stopped, and failed events that
real providers publish, with no fake-only product capability.

- Remove `IAgentReadinessProbe`, `AgentReadinessEvent`, provider probing, generation invalidation, and readiness-event
  projection. No replacement probe, counter, event, or fake-provider branch may be added.
- Remove the fake provider's readiness implementation and control command. Express a busy fake role through an
  outstanding real prompt/session operation and express readiness through `AgentIdleEvent`; recast terminal-session
  stale-event scenarios around ordinary late provider events. Delete the obsolete step and support methods rather
  than retaining aliases.
- Keep `HostLease.SetAgentReadinessProvider`: the host still asks the application for the tri-state result. Unknown
  roles return unknown; startup and active work return not ready; idle returns ready; stopped, failed, and shutdown
  return not ready.
- Update `modules.md`, `test-strategy.md`, and any readiness/session glossary wording so the provider SPI and fake
  event vocabulary match production.

**Acceptance:** `IAgentReadinessProbe` and `AgentReadinessEvent` have no product or test references.
`PromptIsolationAndReadiness`, the readiness scenarios in `HostOwnership`, `TerminalSessionFinality`, healthy
lifecycle, early shutdown, and terminal session/provider scenarios preserve their process-visible outcomes without
new product test hooks.

### Slice 4 - Application commands use one active-session and admission authority

**Outcome:** The application command boundary makes one atomic decision to admit work and select its active session,
and runtime teardown waits for all work admitted by that decision before provider-session disposal.

- Make `SquadViewModel` the sole owner of the active role-session catalog and command admission it already
  coordinates. Construction has no standalone fallback or replaceable admission mode. Under one synchronization
  boundary, reject work after stopping begins or capture the current non-terminal session and register the accepted
  command; a captured session is used only for that tracked command and is not represented by a nominal lease or
  generation.
- Close admission before canceling and draining accepted commands. Keep pending-interaction cancellation and
  handoff wake-ups on the same command path so no UI or handoff operation can select a session after closure, while
  accepted work reaches its defined canceled/completed outcome before runtime disposal.
- Delete `StandaloneSessionAdmission`, `UseAdmission`, `ISessionAdmission`, `SessionLease`, `SessionRegistry`,
  `SessionCatalog`, the unused lifecycle generation/phase/transition machinery, and `SessionGeneration`'s write-only
  session dictionary. Do not relocate a replacement admission interface into another abstraction assembly.
- Give `SessionGeneration` its already assembled `SquadViewModel` collaborator and register sessions directly.
  Give `SquadRuntimeController` the concrete `IWindowHost` collaborator it coordinates and notify it directly.
  Retain the provider-owned `IAgentRuntime.StartAsync` session-publication callback and retain `IRoleNotifier` as the
  delivery-to-application dependency boundary.
- Update `architecture.md`, `modules.md`, and the session-generation glossary to state that the application owns
  active-session command admission while host runtime owns provider generation observation and ordered teardown.

**Acceptance:** There is one active-session collection and one admission decision. Commands accepted before closure
drain or cancel before session disposal; commands and handoff notifications arriving after closure do not reach a
provider session or create transcript/durable side effects. Partial startup still retires every established session,
a failed or stopped role stays unavailable without affecting siblings, and cleanup failure precedence remains
unchanged. The focused gates are `ShutdownCommandAdmission`, `Delivery`, `Recovery`, `TerminalSessionFinality`,
`HeadquartersEarlyShutdown`, `HeadquartersPartialStartupFailure`, `HeadquartersCleanupDiagnostics`, and
`HeadquartersLifecycle`.

After every slice, search all production and specification call sites and remove obsolete overloads, callbacks,
interfaces, records, generated bindings, and public members in that slice. Use the existing black-box Gherkin suite
as the behavior gate. Add or refine a scenario only for an observable invariant not already protected; never
recreate implementation-structure specifications for a removed type.

## Acceptance criteria

- `SquadStartupPlanFactory`, the behavior-carrying `SquadStartupPlan`, `RuntimeMode`, and its one-use `Launch.Create`
  helper are removed.
- Launch preparation produces one immutable role/path/backend-context result and does not expose independently
  replaceable phase delegates or lazy closures over mutable `Ctx.Roles`.
- `WorkspacePreparer` has no failure callback and cannot continue after reporting an invalid workspace.
- `SquadApplication.Create` has no handoff-pump factory, and handoff delivery has no role-provider or arbitrary log
  callback where production has one fixed source and destination.
- `StandaloneSessionAdmission` and `SquadViewModel.UseAdmission` are removed. There is one authoritative active-session
  catalog and one command-admission decision.
- `SessionGeneration` contains no write-only session collection. No lease/generation value remains unless a
  production caller uses it to enforce lifetime safety.
- Provider abstractions contain provider contracts only; host/application admission types are removed or rehomed.
- Host-internal startup methods do not accept callbacks for the one window notification or one session-registration
  behavior.
- `IAgentReadinessProbe` is removed, and `AgentReadinessEvent` remains only if a non-test provider emits it for a
  documented capability. `wait-for-agent` continues to reflect observable idle, busy, stopped, failed, unknown-role,
  startup, and shutdown states.
- Early shutdown still cancels preparation, partial startup disposes every established session, terminal provider
  and handoff failures retain their diagnostics, and cleanup retains its existing failure precedence.
- Handoff delivery and recovery remain durable and restart-safe, and a failed recipient notification does not lose
  or duplicate the handoff.
- Shutdown still drains admitted commands and rejects commands arriving after admission closes without dispatching
  them to a provider session.
- The full backend specification suite and product build pass. No new direct product-object test, friend assembly,
  reflection access, test branch, or replacement injection seam is introduced.