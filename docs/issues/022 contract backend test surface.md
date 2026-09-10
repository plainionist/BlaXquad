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

### Slice 1: Narrow headquarters lifecycle composition

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

### Slice 2: Encapsulate authoritative application and transcript state

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

### Slice 3: Contract the UI protocol and hosting adapters

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

### Slice 4: Reduce the provider implementation to the documented SPI

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

### Slice 5: Remove host-control test seams

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
