---
title: Migrate role interaction specifications
priority: 17
---

# Migrate role interaction specifications

This issue implements the [backend test strategy](../manual/test-strategy.md).

## Goal

Rewrite the prompt, abort, readiness, role-isolation, and pending-interaction scenarios from `ViewModel.feature` as
user-driven exchanges between the headless UI and fake provider.

Retain behavior that a user or provider can observe. Remove scenarios that only prove a private collection, event
counter, semaphore, or method call.

## Acceptance criteria

- Prompts sent as real UI protocol commands reach only the selected fake role session.
- Manual prompts remain serialized per role without blocking independent roles.
- Abort reaches the selected role, cancels the active operation, and leaves the role in the documented subsequent
  state.
- Readiness reflects idle/busy work and remains correct while multiple role sessions start.
- Permission, input, and elicitation requests appear through published UI state and responses return to the owning
  fake session.
- Wrong-role, duplicate, late, aborted, and shutdown interaction responses retain their supported outcomes.
- Identical request IDs for different roles remain isolated where that behavior is supported.
- Provider/session failure produces the documented role availability, error, and command-rejection behavior.
- Steps interact only with the scenario driver's UI and agent APIs.
- Steps do not access `SquadViewModel`, role dictionaries, pending-interaction collections, operation coordinators,
  sessions, or `Recording*` objects.
- Duplicate or implementation-only scenarios are deleted only after their supported behavior is covered through the
  process boundary.

## Implementation plan

Keep the specification boundary in `squad.Specs`: the published `squad-hq` process is driven through
`BackendScenario`, UI actions and observations cross `HeadlessUiClient`, and provider actions and observations cross
`BackendScenarioAgent`. Extend those three semantic facades only where a scenario needs a user- or provider-visible
capability. Raw UI envelopes, fake-provider control messages, process handles, and production object graphs remain
hidden from step definitions.

### Slice 1 (done): Prompt isolation, serialization, and readiness

- Add process-level role-interaction scenarios for a project with two live fake-provider roles.
- Send prompts through `prompt.send` and observe them through the selected role's fake-agent API, including an
  observable negative assertion that the other role did not receive the prompt.
- Keep one role's first prompt active while a second prompt is submitted, proving that the second prompt is not
  delivered until the first operation becomes idle; concurrently prove that an independent role can receive and
  complete its prompt without waiting.
- Observe readiness through the public role status/UI state and `squad-hq wait-for-agent` behavior: a session is not
  ready before idle, becomes ready after idle, becomes busy while a prompt is active, and returns to ready after the
  provider completes that work.
- Start multiple configured role sessions and prove readiness waits remain role-specific while the sessions establish
  and transition independently.
- Remove the covered prompt/readiness scenarios from `ViewModel.feature`, including implementation-only assertions
  about semaphores, overlapping sends, and direct readiness probes.

Acceptance criteria:

- Only the addressed fake role observes each UI prompt.
- Two prompts for one role are delivered in order with no overlap, while another role continues independently.
- Readiness follows provider idle/busy work and remains correct for independently starting role sessions.
- All new steps use only `BackendScenario` UI, agent, CLI, and lifecycle APIs.

**Status: complete (c62ec142c5).** `src/squad.Specs/Features/PromptIsolationAndReadiness.feature` now covers all four acceptance
criteria through the process boundary: prompt isolation, same-role serialization (order, no overlap), a second
role's independent progress while a first role's prompt is still outstanding, and both readiness scenarios.
`StdioWindowHost`'s input pump was changed to dispatch each received line's `UiProtocolSession.ReceiveMessageAsync`
call without awaiting its completion before reading the next line, so no role's outstanding prompt can block
another role's command from being admitted - matching the visual UI's behavior. Dispatched commands are tracked and
drained (with cancellation-and-snapshot performed under one lock to close the admit/drain race) before the session
is disposed on shutdown, and every dispatched task is observed so a fault can never escape unobserved.
`src/squad.Specs/Features/StdioTransportConcurrentDispatchAndShutdown.feature` adds focused stdio-transport coverage
proving a host-control shutdown drains a still in-flight prompt dispatch instead of hanging or crashing. The
original scenario, `Slow sends do not block another role`, has been removed from `ViewModel.feature` now that the
process-boundary scenario covers the same guarantee.

**Architecture decision:** preserve the independent-role acceptance criterion. `StdioWindowHost` must not impose
global command serialization that does not exist in the visual UI. Its input pump owns framing and admission only:
after reading a complete line, it dispatches `UiProtocolSession.ReceiveMessageAsync` without delaying the next read
until that command's domain operation completes. The application remains authoritative for per-role prompt and abort
serialization.

The stdio host must explicitly own the lifecycle of dispatched command tasks. It stops admitting lines on EOF or
shutdown, observes every dispatched task, and drains them before disposing `UiProtocolSession`; no fire-and-forget
faults or command/session disposal races are permitted. Preserve serialized stdout writes and protocol-error
publication. Add focused stdio transport coverage for concurrent command dispatch and shutdown draining, then add
the process-level two-role prompt scenario and remove `Slow sends do not block another role` from
`ViewModel.feature`.

### Slice 2 (done): Published interactions and response ownership

- Add semantic headless-UI observations for pending permission, input, and elicitation state, including input choices,
  freeform support, elicitation mode, URL, and accepted content where those are part of the protocol.
- Have fake roles publish each interaction type, wait for it in `state.snapshot`, answer it through the corresponding
  UI command, and observe the typed response at the requesting fake session.
- Cover role-qualified request ownership: wrong-role responses are rejected without removing the owner's request,
  duplicate and late responses are rejected, and identical request IDs remain independent across roles.
- Cover interaction cancellation caused by abort, session failure, and headquarters shutdown through published UI
  state and fake-session observations rather than pending-interaction collections.
- Remove the covered interaction scenarios from `ViewModel.feature`; retain transcript-retention coverage only where
  it asserts supported transcript behavior rather than collection retention.

Acceptance criteria:

- Published UI state contains every supported field for permission, input, and elicitation requests.
- Successful responses reach only the owning fake role session.
- Wrong-role, duplicate, late, aborted, failed-session, and shutdown responses retain their documented protocol
  errors or cancellation outcomes.
- Equal request IDs in two roles never collide.

**Status: complete (fc64fbbd31).** `src/squad.Specs/Features/PublishedInteractionsAndResponseOwnership.feature`
covers all four acceptance criteria through the process boundary: a two-role backend scenario publishes and observes
every supported permission/input/elicitation field (including input choices, freeform, elicitation mode, URL, and
accepted form content), asserts a successful response reaches only the owning fake role session, exercises
wrong-role/duplicate/late rejection (surfaced as the existing `protocol.error` UI message from
`PendingInteractionRegistry.Remove`) without disturbing the owner's still-pending request, proves identical request
IDs stay independent across two roles, and proves abort, session failure, and headquarters shutdown each cancel a
role's pending interaction so a late response is rejected. `HeadlessUiClient`/`BackendScenario` gained
`WaitForPendingPermission/Input/ElicitationAsync` (polling `state.snapshot`) and a `WaitForProtocolErrorAsync` that
accepts a skip count so a caller can require the next *new* protocol error produced by a later command instead of
re-matching an earlier one already in the buffer; `BackendScenarioSteps` tracks how many protocol errors a scenario
has already observed and passes that count on every subsequent assertion, closing the review finding that the
duplicate/late Then step could pass without the second `permission.respond` actually being rejected.
`FakeAgentSession.CancelPendingInteractionsAsync` now reports a "pending-interactions-cancelled" observation across
the control pipe (mirroring the existing `AbortAsync`/"abort" observation), and the shutdown scenario asserts that
observation in addition to the exit code, closing the review finding that shutdown cancellation was previously
proved only by a clean process exit rather than by any fake-session-visible signal. `FakeAgentSession`/
`FakeProviderControlServer`/`BackendScenarioAgent` also gained response-content plumbing and no-wait
`HasReceivedXResponse()`/`HasObservation()` checks used to prove a role never received another role's response.
The five migrated scenarios ("Interaction requests are visible and can be completed", "Interaction responses are
routed to their owning role", "Interaction responses reject wrong-role and late completions", "Abort and shutdown
cancel pending interactions", "The same request ID can be pending for two roles independently") were removed from
`ViewModel.feature`, along with their now-orphaned step methods in `ViewModelSteps.cs` and the now-unused
response-tracking queues on `RecordingAgentSession`. "Failed role state is terminal and clears its pending
interactions" and "Pending interaction context remains retained" were intentionally kept: the former belongs to
Slice 3's broader session-failure scope, and the latter asserts genuine transcript-retention behavior rather than
pending-interaction collection retention.

### Slice 3 (in progress): Abort sequencing and terminal role failure

- Extend the fake-agent API with deterministic, acknowledged controls for an abort that is pending, completes, or
  fails, without adding product test hooks or arbitrary sleeps.
- Drive `role.abort` through the UI and prove only the selected fake session receives it, its active prompt operation
  is cancelled, stale events from that turn do not reach published state, and a following prompt waits for abort
  completion before delivery.
- Cover repeated idle aborts and retry after a failed abort by their user-visible command results and subsequent role
  availability, replacing assertions about abort counters or coordinator state.
- Fail one fake role session while another remains healthy, then verify the failed role's published status and error,
  removal of its pending interactions, rejection of later commands, suppression of late provider events, and
  continued prompt handling by the healthy role.
- Remove the remaining covered abort, role-isolation, and session-failure scenarios from `ViewModel.feature`; delete
  scenarios that have no supported process-boundary behavior instead of recreating private method-call assertions.

Acceptance criteria:

- Abort is role-isolated, cancels active work, and leaves the role in the documented state for the next command.
- Prompt delivery cannot overtake an in-flight abort, and stale cancelled-turn output is not published.
- Failed abort and repeated abort outcomes remain observable without inspecting coordinator internals.
- A terminal session failure affects only its role, publishes its error and availability, rejects unsupported later
  commands, and leaves other roles operational.

**Status: complete (c7a7aa3042).** `src/squad.Specs/Features/AbortSequencingAndTerminalRoleFailure.feature` covers
all four acceptance criteria through the process boundary: abort routed to only the addressed fake role while
another role's session is untouched; aborting a role with an outstanding prompt cancels that prompt's operation,
proved by successfully sending and observing a following prompt rather than by polling for a "ready" status (an
invalidated role has no mechanism to become ready on its own - only dispatching a new prompt clears the
invalidation); a following prompt genuinely waits for an in-flight abort to finish before it is delivered; events
published during a cancelled turn are ignored; repeated idle aborts are safe and each is independently observable;
a failed abort can be retried and a following prompt then succeeds; and a terminal session failure affects only its
own role's published status/error, rejects later commands for that role, and leaves the other role operational.
`FakeAgentSession` gained deterministic, acknowledged abort controls (`arm-pending-abort`, `complete-pending-abort`,
`fail-pending-abort`, `fail-next-abort`) driven across the existing control transport, with no product test hooks
or arbitrary sleeps. Fixing these scenarios also found and fixed a genuine deadlock in `FakeAgentSession.AbortAsync`:
it cleared its pending-abort field via `Interlocked.Exchange` before awaiting it, so a later
`complete-pending-abort`/`fail-pending-abort` control read an already-null field and silently no-opped, leaving the
abort - and anything waiting on it - hung forever; it now keeps a local reference to the pending abort while
awaiting it and only clears the field afterward via `CompareExchange`, so it cannot clobber a freshly re-armed
instance. The 7 migrated scenarios ("Failed role state is terminal and clears its pending interactions", "Abort is
routed to the matching role", "Cancelling an active prompt cancels its local operation", "A prompt waits for an
in-flight cancellation to finish", "Events from a cancelled turn are ignored", "Repeated idle cancellation is
safe", "A failed cancellation can be retried") were removed from `ViewModel.feature`, along with their now-orphaned
step methods and fields in `ViewModelSteps.cs` and the abort test controls they exclusively exercised
(`BlockAbort`/`FailAbort`/`AbortEntered`/`ReleaseAbort`/`AbortCount`) on `RecordingAgentSession`.

Review (d748d5b0e6) found that the terminal-failure scenario's pending-permission removal proof was invalid:
`WaitForNoPendingPermissionAsync` matched the first `state.snapshot` anywhere in stdout lacking the permission,
including the `ui.ready` handshake snapshot recorded before the permission ever existed, so a session failure that
left the permission published would still pass. Fixed by adding `WaitForLatestStateSnapshotAsync`, which evaluates
its predicate only against the most recently published snapshot on each poll, and routing
`WaitForNoPendingPermissionAsync` through it - proving the *current* published state no longer contains the
permission rather than that some earlier snapshot happened not to.

### Completion

After all slices are accepted, confirm no migrated step references `SquadViewModel`, role or interaction collections,
operation coordinators, sessions, or `Recording*` fixtures. Remove this issue only after the remaining
`ViewModel.feature` scenarios are outside this issue's prompt, abort, readiness, role-isolation, and interaction
scope.

**Status: complete.** All three slices are accepted. The remaining `ViewModel.feature` scenarios cover startup and
shutdown lifecycle ordering, transcript rendering and retention, tool/console activity formatting, and snapshot
content - none reference this issue's prompt, abort, readiness, role-isolation, or pending-interaction scope, and
none of the surviving steps used by them were introduced or retained by this migration to bypass the process
boundary. This issue can be closed.

