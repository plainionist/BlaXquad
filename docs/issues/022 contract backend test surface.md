---
title: Contract the backend surface exposed for tests
priority: 22
---

# Contract the backend surface exposed for tests

This issue implements the [backend test strategy](../manual/test-strategy.md).

## Goal

Remove production API, indirection, nullability, callbacks, overloads, and state exposure that no longer has a
production caller after the specification migration.

This is the final behavior-preserving cleanup. Determine the final surface from production call sites rather than
preserving members because old tests once used them.

Likely candidates include:

- direct `SquadApplication` construction and inspection properties;
- public `SquadViewModel` state collections and test event/request injection;
- test-only lifecycle and event sinks;
- optional `viewModel`, `hostLease`, preparation, and message callbacks;
- public protocol publishers, journals, serializers, and recovery helpers;
- host lease, window host, sleep inhibitor, and event-channel observation properties;
- test-only handoff poller overloads and stop delegates; and
- provider session capacities, timeouts, and callbacks not varied by production.

Private delegates that are the simplest implementation of one cohesive responsibility and nullable values that model
real domain absence are not targets.

## Implementation plan

Treat the published executables, their command-line and wire protocols, and the documented provider/UI contracts as
the supported boundaries. For each product assembly below, classify every public type and member from its production
call sites: remove dead members, make assembly-local implementation types internal, and retain cross-assembly members
only when the production composition root or a documented SPI consumes them. Do not replace removed access with
friend assemblies, reflection, test switches, or another facade.

### Slice 1 [done]: Narrow headquarters lifecycle composition

1. Replace the two `SquadApplication` construction paths with the single production composition path used by
   `squad-hq`; remove the direct constructor, optional event sink, optional view-model and host-lease branches, and
   the internal constructor shape that exists only to support those alternatives.
2. Remove `SquadApplication.ViewModel`, `SquadApplication.Sessions`, and the corresponding session projections from
   `SquadRuntimeController` and `SessionGeneration`; keep session ownership private to the active runtime generation.
3. Remove the test-oriented `RunAsync` readiness callback and the unused `RunResult` return contract. Preserve the
   existing startup/shutdown race semantics, terminal failure precedence, and ordered cleanup while adapting the
   production launch caller.
4. Make the startup plan and its composition factory exact production contracts: remove the optional empty-context
   preparation path, make required preparation explicit, and internalize the executable-local factory. Remove unused
   synchronous workspace-preparation variants and assembly-local workspace helpers exposed by the former white-box
   suite.
5. Run the healthy, early-shutdown, partial-startup-failure, termination, handoff-pump-failure, and terminal-provider-
   failure headquarters scenarios together.

**Slice acceptance:** Headquarters retains its externally observed startup, failure, shutdown, and cleanup behavior
through the published process while `SquadApplication` exposes no construction alternative, callback, result, or
session/view-model inspection surface used only by tests.

**Status: complete (d18b26c889).** `SquadApplication` is composed only through `Create` with required collaborators.
The direct constructor, optional event sink, optional view-model and host-lease branches, `ViewModel`/`Sessions`
projections, `RunAsync` readiness callback, and `RunResult` are removed. Session ownership stays private to the
active runtime generation. `SquadStartupPlan` requires context preparation, `SquadStartupPlanFactory` is internal,
and unused synchronous workspace-preparation helpers are gone.

### Slice 2 [done]: Encapsulate authoritative application and transcript state

1. Remove `SquadViewModel.Roles`, the pending-permission/input/elicitation collections,
   `TranscriptHistoryDirectory`, `StateChanged`, and other direct mutable or observational state surfaces that have
   no production caller.
2. Remove provider-event and interaction-request injection methods and the role-less interaction-completion
   overloads. Retain the role-qualified operations required by `ISquadUi`, the transcript operations required by
   `ITranscriptUi`, and the lifecycle/session operations called by `squad.Host.Runtime`.
3. Collapse `SquadViewModel` construction to the production retention policy instead of accepting caller-selected
   retention settings. Keep all real absence values in snapshots, interactions, and transcript entries nullable.
4. Make `AgentRoleState` and any transcript archive/state/result members that do not cross a production assembly
   boundary internal; remove archive paths, entry collections, or tuning members whose only purpose was direct test
   inspection.
5. Run the prompt, abort, interaction, role-failure, readiness, transcript-retention, transcript-history, and
   transcript-synchronization scenarios through `BackendScenario`.

**Slice acceptance:** All role, interaction, and transcript behavior remains observable through the real UI and
provider protocols, while application state can no longer be read, mutated, or supplied directly for tests.

**Status: complete (734e65651a).** `SquadViewModel` no longer exposes `Roles`, the pending-permission/input/elicitation
collections, `TranscriptHistoryDirectory`, or `StateChanged`; `CreateSnapshot` reads directly from the interaction
registry. `GetRoleReadiness` and `BeginStopping` are private, the role-less permission/input/elicitation completion
overloads and the `RequestPermissionAsync`/`RequestInputAsync`/`RequestElicitationAsync` injection methods are gone,
and completion now requires a non-null expected role throughout `SquadViewModel` and `PendingInteractionRegistry`.
Construction always uses the production retention policy (`ValidateTranscriptRetentionOptions` removed).
`AgentRoleState` is internal with its unused `TranscriptEntries` property removed; `RoleTranscriptState.Entries` and
`TranscriptArchive.DirectoryPath` are removed as they crossed no production assembly boundary. Prompt, abort,
interaction, role-failure, readiness, transcript-retention, transcript-history, and transcript-synchronization
scenarios pass through `BackendScenario` (verified via targeted `dotnet test` filters covering all of these areas).

### Slice 3 [done]: Contract the UI protocol and hosting adapters

1. Make `SnapshotPublisher`, `TranscriptAnnouncementJournal`, `TranscriptProtocol`,
   `TranscriptRecoveryAnnouncement`, `SequencedTranscriptAnnouncement`, and other protocol implementation helpers
   internal; leave `UiProtocolSession` as the assembly-crossing protocol component used by both production hosts.
2. Remove the `BLAXQUAD_PHOTINO_SMOKE` branch and its shutdown callback. Remove URL-opening and serialized-message
   injection overloads that exist only for direct tests while retaining the production URL behavior and the
   transport output callback required by the two real hosts.
3. Collapse `PhotinoWindowHost` to its production constructor and make `UiReady`, `ReceiveMessageAsync`, title
   creation, and similar adapter internals non-public. Remove `IWindowHost.HasCloseSignal`, which no lifecycle caller
   observes.
4. Remove `ISleepInhibitor.CommandPrefix` and keep command detection private to `SleepInhibitor`; preserve
   `IWindowHost` and `ISleepInhibitor` as the production contracts consumed by runtime composition.
5. Run UI selection, stdio transport, protocol validation, transcript paging/recovery/ordering, and shutdown-with-
   open-stdin scenarios together.

**Slice acceptance:** Photino and stdio continue to expose the same versioned UI behavior, but only the production
host/protocol contracts remain public and no test environment branch or callback remains.

**Status: complete (59a8a068b0).** `SnapshotPublisher`, `TranscriptAnnouncementJournal`, `TranscriptProtocol`,
`TranscriptRecoveryAnnouncement`, and `SequencedTranscriptAnnouncement` are internal to `squad.Ui.Protocol`;
`UiProtocolSession` remains the assembly-crossing protocol component used by `PhotinoWindowHost` and
`StdioWindowHost`. The `BLAXQUAD_PHOTINO_SMOKE` branch and its shutdown callback are removed from
`UiCommandHandler`/`UiProtocolSession`, along with the injectable URL-opener and serialized-message-sink
constructor parameters that existed only for direct tests; `UiCommandHandler` always opens URLs through its own
`OpenExternalUrl` helper and both hosts communicate only through the production `sendSerializedMessage` callback.
`PhotinoWindowHost` now has a single production constructor, with `CreateTitle` and `ReceiveMessageAsync` private
and the unused `UiReady` property removed. `IWindowHost.HasCloseSignal` is removed (no lifecycle caller observed
it) along with both hosts' implementations. `ISleepInhibitor.CommandPrefix` is removed from the interface and
`SleepInhibitor` keeps command-prefix detection private. Verified via targeted `dotnet test` filters covering UI
selection, stdio transport, protocol validation, transcript paging/recovery/ordering, and shutdown-with-open-stdin
scenarios, plus a Headquarters regression pass.

### Slice 4 [done]: Reduce the provider implementation to the documented SPI

1. Keep `IAgentProviderFactory`, `IAgentBackend`, `IAgentRuntime`, `IAgentSession`, readiness/failure capabilities,
   and typed `AgentEvent` values as the provider SPI used by the production and test providers.
2. Make `CopilotSdkBackend`, `CopilotSdkAgentSession`, tool-output normalization, and other concrete Copilot helpers
   internal; only the reflection-loaded `CopilotSdkAgentProviderFactory` remains public from that implementation
   assembly.
3. Remove caller-configurable event capacity, write timeout, teardown timeout, publication methods, callbacks, and
   observation properties that production never varies or calls. Keep fixed bounded-backpressure and teardown
   policies private, including the internal failure escalation needed by the owning runtime.
4. Internalize or relocate `AgentEventChannel` if it is only a Copilot implementation helper, and remove unused
   event variants that no production or test provider emits. Do not narrow members required by the fake provider's
   documented control protocol, including session identity and typed event publication.
5. Run provider loading/packaging, fake-provider control, active usage, overload/failure, and session cleanup
   scenarios together, including the provider-free `squad-hq` publication.

**Slice acceptance:** Provider selection and the test-owned fake providers still work solely through the documented
provider SPI, while Copilot implementation details and production-invariant tuning controls are no longer public.

**Status: complete (db3bd99e71).** `CopilotSdkBackend`, `CopilotSdkAgentSession`, and `CopilotToolOutputNormalizer` are internal;
only the reflection-loaded `CopilotSdkAgentProviderFactory` remains public from `squad.CopilotSdk`. `AgentEventChannel`
was relocated from `squad.AgentProvider.Abstractions` into `squad.CopilotSdk` as an internal type, since it is a pure
Copilot implementation helper with no test-suite dependency (the fake provider uses its own independent
`TestAgentEventStream`). Its unused `Depth` observation property is removed, and its caller-configurable `capacity`
and `writeTimeout` constructor parameters, along with `CopilotSdkAgentSession`'s `capacity`/`writeTimeout`/
`failureTeardownTimeout` parameters, are now fixed internal policy since no production or test call site ever varied
them; the genuinely necessary `escalateTeardownFailure` callback and `onOverload` callback remain. `AgentEventError`,
an `AgentEvent` variant no production or test provider ever constructed, is removed along with its dead consumption
in `SquadViewModel.IsImmediateUiEvent` and the duplicate `AgentEventProjector` case. Verified via targeted
`dotnet test` filters covering provider loading/packaging, fake-provider control, active usage, overload/failure,
session cleanup, and the provider-free `squad-hq` publication, plus a Headquarters regression pass.

### Slice 5 [done]: Remove host-control test seams

1. Replace the test-substitution-only `IHostLease` indirection with the concrete production lease in headquarters
   lifecycle composition and make lease ownership mandatory after successful acquisition.
2. Remove `HostLease.PipeName` and restrict cleanup-lease construction, pipe-name derivation, lock probing, stale
   metadata removal, and raw shutdown helpers to the smallest visibility required by `HostControlClient`.
3. Keep only the public client operations called by the `squad-hq shutdown` and `wait-for-agent` commands; do not
   expose server tasks or lock state beyond the runtime owner that must observe them.
4. Run host ownership, coexistence, shutdown admission, cleanup diagnostics, and healthy relaunch-after-clean-shutdown
   scenarios. This verifies existing lifecycle behavior only and must not add the separate UI relaunch behavior from
   `restart button.md`.

**Slice acceptance:** The host-control command boundary preserves ownership, readiness, shutdown, and stale-state
behavior while no public lease probe, observation property, or alternate lease implementation remains solely for
tests.

**Status: complete (b3cdce5cb9).** `IHostLease` is removed; `squad.Host.Runtime.SquadApplication` and
`squad-hq`'s `Launch` composition now depend on the concrete `HostLease` directly, so successful acquisition always
transfers ownership to a real lease rather than a substitutable abstraction. `HostLease.PipeName` is removed (it had
no external reader), and `RemoveStaleMetadata`, `TryAcquireProbe`, `TryAcquireCleanupLease`, and `PipeNameFor` are
now `internal`, used only by `HostControlClient` within `squad.Host.Control`. `CleanupLease` is now an internal
type, constructed and consumed only inside `squad.Host.Control`. `HostControlClient.RequestShutdownAsync` is now
private, called only by the public `ShutdownAsync`; `WaitForAgentAsync` and `ShutdownAsync` remain the only public
client operations, matching the `squad-hq wait-for-agent` and `shutdown` commands. Verified via targeted
`dotnet test` filters covering host ownership, coexistence, shutdown admission, and cleanup diagnostics scenarios,
plus a Headquarters regression pass (including the existing-inbox-survives-restart scenario covering healthy
relaunch after clean shutdown). No restart-button/UI-relaunch behavior from `restart button.md` was introduced.

### Slice 6: Remove handoff-delivery test seams and close the surface audit

1. Replace the test-substitution-only `IHandoffPump` abstraction with the production `InProcessHandoffPoller` where
   no second production implementation exists, while retaining `IRoleNotifier` as the real cross-module callback
   from delivery to the active role sessions.
2. Remove the fixed-role `InProcessHandoffPoller` constructor overload and the `HandoffDeliveryService.ProcessOnceAsync`
   stop delegate; production uses the live role provider and cancellation token.
3. Restrict delivery helpers and any remaining configuration, workspace, process, or handoff types/members to their
   actual assembly callers. Preserve public values that necessarily cross production assembly boundaries, and do
   not remove command/protocol data merely because callers rely on inferred return types.
4. Audit the final product build for public constructors, methods, properties, events, delegates, nullable
   collaborators, overloads, capacities, and timeouts. For every survivor, identify its production call site or its
   documented provider/UI SPI role in `docs/manual/test-strategy.md`; update that document only where an intentional
   SPI needs clearer enumeration.
5. Run delivery and recovery scenarios, then the complete backend specification suite and product build. Confirm the
   specs retain only the provider-abstractions project reference and that no forbidden test access mechanism or
   product-side fake remains.

**Slice acceptance:** Real filesystem handoff delivery remains durable and restart-safe, the final backend public
surface is justified entirely by production composition or documented provider/UI SPIs, and the complete process-
boundary suite passes without a supported behavior change.

## Acceptance criteria

- Every remaining public member has a production caller or is an intentional provider/UI SPI documented in
  `docs/manual/test-strategy.md`.
- No product constructor or method accepts a delegate, nullable collaborator, timing knob, or alternate implementation
  solely for tests.
- `SquadApplication.ViewModel`, `SquadApplication.Sessions`, direct mutable role/pending-interaction inspection, and
  comparable white-box probes are removed.
- Protocol and lifecycle helpers used only inside their owning assemblies are internal.
- No `InternalsVisibleTo`, reflection-based access, `ForTests` API, test environment branch, or replacement test facade
  is introduced.
- Provider selection and the headless UI remain cohesive production SPIs; the fake provider and its control protocol
  remain entirely in `squad.Specs`.
- The change is a net reduction in public members, overloads, optional branches, and injected callbacks.
- All backend specifications continue to pass through the process boundary with no supported behavior change.
- No relaunch or frontend behavior from `restart button.md` is introduced.
