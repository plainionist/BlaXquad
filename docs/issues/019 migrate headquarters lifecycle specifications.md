---
title: Migrate headquarters lifecycle specifications
priority: 19
---

# Migrate headquarters lifecycle specifications

This issue implements the [backend test strategy](../manual/test-strategy.md).

## Goal

Rewrite startup, shutdown, cancellation, provider failure, resource cleanup, generation isolation, and command-draining
scenarios from `ViewModel.feature` around observable behavior of the real `squad-hq` process.

Use the fake provider and headless UI to control meaningful asynchronous boundaries. Replace exact internal lifecycle
traces with outcomes that prove safety: process termination, error reporting, command rejection, absence of stale
updates, released host ownership, and successful subsequent launch.

This issue must not add relaunch behavior from `restart button.md`.

## Acceptance criteria

- Healthy startup reaches UI and agent readiness and shuts down cleanly.
- Shutdown requested before or during startup prevents the host from becoming available and releases acquired
  resources.
- Window/input closure, caller cancellation, host-control shutdown, provider failure, and handoff failure produce the
  documented terminal result and diagnostics.
- Partial provider startup is retired and does not leave a live session or owned host behind.
- Accepted commands reach a safe terminal outcome before provider resources disappear; newly rejected commands have no
  side effects.
- Delayed events from a terminated session cannot change subsequently published state.
- Primary and cleanup failures remain observable without requiring injected exception collectors.
- Resource release is proven by process exit, closed control endpoints, durable state, and the ability to start a new
  host.
- Scenarios do not construct `SquadApplication`, inject startup delegates, inspect session registries, or assert
  `LifecycleTrace` entries.
- Exact interleavings or callback order without a user-visible or resource-safety consequence are deleted.
- Existing supported lifecycle behavior remains unchanged.

## Implementation plan

All migrated scenarios run the published, provider-free `squad-hq --ui stdio` executable through
`BackendScenario`. They observe only process results, standard-error diagnostics, the versioned UI protocol, the
host-control commands, durable workspace state, and the fake-provider control pipe. Test support may expose semantic
startup gates and provider outcomes, but it must remain entirely in `squad.Specs`; production assemblies receive no
test callbacks, injectable lifecycle delegates, or failure switches.

Each terminal scenario proves the relevant subset of the same resource-safety contract: the launched process exits
within a bounded deadline, started fake sessions report disposal, the host-control endpoint becomes unavailable,
durable workspace and handoff state is preserved, and a healthy replacement process can acquire the same project and
reach readiness. Keep normal cleanup separate from `BackendScenario.Dispose`, which remains emergency-only cleanup.
Migrate and delete each covered `ViewModel.feature` scenario and its orphaned bindings/support in the same slice.
Delete exact callback-order assertions rather than recreating `LifecycleTrace` through another test API.

This issue does not add a runtime replacement command, a `Relaunching` phase, `canRelaunch`, or any toolbox/UI
behavior from `restart button.md`. A subsequent process launch is used only to prove that the terminated process
released ownership.

### Slice 1 [done]: Prove healthy startup and host-controlled shutdown

1. Extend the process scenario facade with semantic observations for UI readiness, every configured provider session
   becoming ready, process completion, session disposal, host-control unavailability, and a subsequent launch against
   the same workspace.
2. Specify a multi-role launch that completes the real `ui.ready` handshake, observes each fake session and its
   initial instruction, reaches agent readiness through `squad-hq wait-for-agent`, and shuts down through
   `squad-hq shutdown`.
3. Assert zero exit, disposed sessions, released host ownership/control, preserved seeded durable state, and a
   successful fresh launch.
4. Remove the covered happy-path `SquadApplication`, host-lease, session-draining, prepared-role ordering, and
   lifecycle-trace scenarios from `ViewModel.feature`. Delete the exact startup/teardown trace scenario instead of
   translating its callback order.

**Slice acceptance:** A real multi-role headquarters process reaches UI and agent readiness, shuts down through its
public control command, preserves durable state, releases all externally observable ownership, and permits a fresh
healthy launch.

**Status: complete (c43fde8f6c).** `HeadquartersLifecycle.feature` covers slice 1 through the process boundary: a
real multi-role `squad-hq` launch completes `ui.ready`, observes both fake sessions and their initial harness
instructions, reaches agent readiness through `squad-hq wait-for-agent` after idle, shuts down through
`squad-hq shutdown` with exit 0, reports both sessions disposed, finds host control unavailable, preserves seeded
`notes.md` in the coder worktree, and a replacement Echo-provider process reaches `ui.ready` on the same workspace.
Covered ViewModel happy-path `SquadApplication`, host-lease, session-draining, prepared-role ordering, and
lifecycle-trace scenarios were removed with their orphaned bindings.

### Slice 2 [done]: Terminate cleanly on UI closure and caller cancellation

**Decision:** Keep the existing `Process.Start` path for every ordinary `BackendScenario` launch. Add a dedicated,
test-owned cancellation-capable process launcher used only by the caller-cancellation scenario. It may use the
platform's native process-group/session primitive to isolate the child and deliver the normal interrupt signal
(`CTRL_BREAK_EVENT` to a Windows `CREATE_NEW_PROCESS_GROUP`, `SIGINT` to a Unix process group), while preserving
redirected standard streams and the same published executable/arguments. Hide native handles, signal mechanics,
and process-group setup behind one narrow support type in `squad.Specs`; do not add a product command, environment
switch, or in-process `Launch.Run` shortcut. If a platform cannot provide isolated delivery, fail the test with an
explicit unsupported-platform diagnostic rather than signaling the shared test-runner group. This complexity stays
in Slice 2 because it is the only supported boundary that proves the executable's existing
`Console.CancelKeyPress` behavior.

1. Add semantic `BackendScenario` operations to close the stdio input and to deliver the platform's normal
   cancellation signal to the exact launched child process; keep platform mechanics and raw process access out of
   steps.
2. Specify input closure after readiness and before `ui.ready`, and caller cancellation after readiness. Observe the
   documented exit result and diagnostics rather than an internal `RunResult` or callback count.
3. Prove each path disposes started sessions, releases the control endpoint and host ownership, and allows a later
   healthy process to start.
4. Consolidate or remove overlapping raw stdio scenarios, and remove the covered window-close, caller-cancellation,
   readiness-callback, and session-draining scenarios and bindings from `ViewModel.feature`. Delete the synthetic
   recording-window close-failure case because no supported process boundary can produce it.

**Slice acceptance:** Closing the headless UI or cancelling the launched command terminates only that headquarters
process, reports the documented result, cleans up owned resources, and does not require a recording window or injected
caller token.

**Status: complete (b945cabe31).** `HeadquartersTermination.feature` covers slice 2 through the process boundary:
closing stdin after readiness and before `ui.ready`, and delivering the platform cancellation signal after
readiness, each exit 0, and the after-readiness paths also dispose both fake sessions, find host control
unavailable, and allow a replacement Echo-provider process to reach `ui.ready` on the same workspace. Caller
cancellation uses a dedicated `CancellableChildProcess` launcher only (ordinary `Process.Start` unchanged), per
the slice decision. Covered ViewModel window-close, caller-cancellation, and synthetic window-close-failure
scenarios were removed, along with the overlapping StdioUiProtocol end-of-input scenarios.

### Slice 3 [done]: Stop safely before and during startup

1. Split process launch from readiness completion inside the test facade so scenarios can use the real host-control
   endpoint while startup is awaiting `ui.ready` or a fake-provider startup gate.
2. Extend the fake provider control protocol with acknowledged, test-owned startup gates at meaningful provider
   boundaries, including after one session has started; pass configuration only through environment understood by
   the test provider.
3. Specify shutdown requested immediately after host acquisition, while waiting for UI readiness, and while provider
   startup is blocked. Assert that readiness is never published after the request, no new command reaches a provider
   session, any partially started session is disposed, and the process exits cleanly.
4. Prove preserved durable state, closed host control, and successful acquisition by a later healthy launch. Remove
   the covered pre-start shutdown, blocked-preparation, shutdown-already-requested, and simultaneous-ready
   `SquadApplication` scenarios and bindings.

**Slice acceptance:** A real host-control shutdown linearized anywhere before readiness prevents the host from
becoming available and releases every resource already acquired.

**Status: complete (78dca103fb).** `HeadquartersEarlyShutdown.feature` covers slice 3 through the process boundary:
shutdown as soon as host-control is reachable (without completing `ui.ready`) and shutdown while the fake provider
is gated after `coder` starts and before `reviewer` starts. Both race a concurrent `squad-hq wait-for-agent` that
never reports ready, send a UI prompt that never appears on the control pipe, exit 0, leave host control
unavailable, preserve seeded `notes.md`, and allow a replacement Echo-provider process to reach `ui.ready`. The
gated path also proves `reviewer` never started and that the started `coder` session is disposed. Covered ViewModel
pre-start shutdown, blocked-preparation, shutdown-already-requested, and simultaneous-ready scenarios were removed.
The follow-up `78dca103fb` closes the review findings that readiness was only inferred from later unavailability,
that the one-session gate was not observed, and that no post-shutdown command was shown to miss the provider.

### Slice 4 [done]: Retire failed and partial provider startup

1. Add fake-provider modes for failure before runtime availability and failure after a configured number of sessions
   have started. The fake runtime must report session start/disposal over its private control pipe and retain no
   product-side test seam.
2. Specify both failure points through the real executable, asserting non-zero exit and a clear provider diagnostic
   on standard error.
3. For partial startup, prove every created session is disposed, no role becomes available through host control, no
   live host remains, durable state survives, and a healthy provider can subsequently launch on the same project.
4. Remove the covered SDK-shaped unwind, before/after-window startup failure, CLI exception-type, partial-backend
   startup, and partial-start trace scenarios and their obsolete recording-provider bindings. Reuse the existing
   provider-selection specifications where they already prove an equivalent public diagnostic instead of duplicating
   them.

**Slice acceptance:** Provider creation or startup failure, including failure after one session starts, exits with a
useful diagnostic and conclusively retires all partial provider and host resources.

**Status: complete (bd5f1fadde).** `HeadquartersPartialStartupFailure.feature` covers slice 4 through the process
boundary: fake-provider env-driven failure before runtime (`BLAXQUAD_FAKE_FAIL_BEFORE_RUNTIME`) and after one session
(`BLAXQUAD_FAKE_FAIL_AFTER_SESSIONS`). Both real `squad-hq` launches exit non-zero with a "Provider startup failed"
stderr diagnostic (never an "Unhandled exception" dump). The partial path observes `coder` started then disposed over
the control pipe and `reviewer` never started, then host control unavailable, seeded `notes.md` preserved, and a
replacement Echo-provider process reaches `ui.ready`. `Launch.cs` maps `RunAsync` provider/runtime failures to
`CliExitException`. Covered ViewModel SDK-shaped unwind, before/after-window startup failure, CLI exception-type,
partial-backend startup, and partial-start trace scenarios were removed with `LifecycleTrace` and
`FailAfterCreatingSessionCount` wiring.

### Slice 5 [done]: Surface terminal provider failures after readiness

1. Make the fake backend expose a controllable provider-wide terminal failure through
   `IAgentBackendFailureSource`, independently from the existing per-session completion/failure controls.
2. Specify that a per-session failure remains visible as that role's terminal UI state without becoming a cleanup
   error, while a provider-wide terminal failure ends the process with the original diagnostic.
3. Cover a provider failure racing normal shutdown and assert the documented primary outcome without inspecting
   exception types or injected collectors.
4. Prove session disposal, host-control closure, durable-state preservation, and successful subsequent launch, then
   remove the covered backend-wide failure, SDK-shaped session error, and normal-shutdown session-failure scenarios
   and bindings from `ViewModel.feature`.

**Slice acceptance:** Session and provider terminal failures retain their distinct supported outcomes, and a
provider-wide failure is diagnosable while still releasing the failed host completely.

**Status: complete (5612072b88).** `HeadquartersTerminalProviderFailure.feature` covers slice 5 through the process
boundary: a per-session fake-provider failure marks only that role `error` while another role still takes work and a
later host-control shutdown exits 0 and disposes both sessions; a backend-wide `IAgentBackendFailureSource` failure
exits non-zero with the original diagnostic (`shared SDK force-stop failed`), not an unhandled dump and not
`Provider startup failed`, disposes started sessions, leaves host control unavailable, preserves seeded `notes.md`,
and allows a replacement Echo-provider process to reach `ui.ready`. The same backend-wide outcome remains primary
when shutdown is also requested. Covered ViewModel backend-wide failure, SDK-shaped session error, and
normal-shutdown session-failure scenarios were removed. The follow-up `5612072b88` closes the review finding that
post-ready provider failures were mislabeled as startup failures.

### Slice 6 [done]: Surface a real handoff-pump failure

1. Arrange a deterministic filesystem-backed handoff polling failure using the real workspace and delivery pump,
   rather than injecting `IHandoffPump.Failure` or constructing `SquadApplication`.
2. Trigger the failure after readiness and assert that the process exits non-zero with the handoff diagnostic while
   the durable handoff artifact remains in a recoverable state.
3. Prove provider-session disposal, closed host control, released ownership, and successful continuation by a healthy
   subsequent process.
4. Remove the recording-pump post-ready failure scenario and any support made orphaned by it.

**Slice acceptance:** An unexpected real handoff-pump failure terminates headquarters visibly without losing durable
work or retaining process, provider, endpoint, or host ownership.

**Status: complete (6ed5523b84).** `HeadquartersHandoffPumpFailure.feature` covers slice 6 through the process
boundary: after a real `squad-hq` launch, a durable inbox handoff is seeded and the role outbox is replaced with a
broken directory junction so the real `InProcessHandoffPoller` faults. The process exits non-zero with
`Handoff delivery failed` (not an unhandled dump and not `Provider startup failed`), disposes the started session,
leaves host control unavailable, preserves the queued inbox artifact, and after repairing the outbox a replacement
Echo-provider process reaches `ui.ready`. Covered ViewModel post-ready handoff-failure scenario and
`RecordingHandoffPump.Fail` were removed.

### Slice 7: Drain accepted commands and reject new commands during shutdown

1. Extend the fake provider with acknowledged gates for a prompt that remains in flight until cancellation and for
   runtime disposal, allowing shutdown admission and retirement boundaries to be observed without sleeps.
2. Send one accepted prompt, request host-control shutdown, and prove that prompt reaches a safe terminal outcome
   before the fake session is disposed.
3. While cleanup is held at the provider boundary, send another protocol command and assert an explicit rejection
   with no prompt, interaction, transcript, or durable side effect.
4. Complete cleanup and prove bounded zero exit and released ownership. Remove the white-box command rejection,
   command-drain, and blocking-cleanup timing scenarios and bindings from `ViewModel.feature`.

**Slice acceptance:** Shutdown cancels or completes every admitted command before provider resources disappear, while
commands arriving after admission closes are observably rejected and side-effect free.

### Slice 8: Ignore events from terminated sessions

1. Add fake-provider operations that terminate a session and then attempt delayed assistant, readiness, interaction,
   and completion-failure publication from that same session identity.
2. Capture the role's terminal protocol state, publish the delayed events, request a fresh state/transcript
   synchronization, and prove none of the stale values changes the published role, transcript, or interactions.
3. Shut down normally and prove the stale publisher cannot prevent disposal or host release.
4. Remove the covered late-event and open-event-observer white-box scenarios and their recording-session controls.

**Slice acceptance:** Once a provider session terminates, delayed work carrying its identity cannot mutate any later
published state or obstruct process cleanup.

### Slice 9: Preserve primary and cleanup diagnostics through final release

1. Add fake-provider failure modes that independently fail a primary startup/runtime operation and one or more
   provider cleanup stages, with distinct diagnostic messages and acknowledged entry into cleanup.
2. Specify startup-plus-cleanup and runtime-plus-cleanup failures through the executable. Assert all primary and
   cleanup messages on standard error and a non-zero exit without depending on aggregate exception structure.
3. Hold provider cleanup at an acknowledged boundary and prove the host remains owned while its resources are still
   live; after release, prove the process exits, the endpoint and metadata are gone, durable state is intact, and a
   healthy provider can launch on the same workspace.
4. Remove the covered primary/cleanup aggregation, runtime/cleanup aggregation, cancellation-failing startup, and
   backend-cleanup ownership scenarios and bindings. Delete remaining lifecycle-only recording fakes,
   `LifecycleTrace`, direct `SquadApplication` construction, startup delegates, and session-registry inspection once
   no non-lifecycle scenario uses them.

**Slice acceptance:** Primary and cleanup failures are all visible in process diagnostics, cleanup continues through
every owned resource, and host ownership is released only after provider retirement actually finishes.
