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

### Slice 2 (pending): Published interactions and response ownership

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

### Slice 3 (pending): Abort sequencing and terminal role failure

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

### Completion

After all slices are accepted, confirm no migrated step references `SquadViewModel`, role or interaction collections,
operation coordinators, sessions, or `Recording*` fixtures. Remove this issue only after the remaining
`ViewModel.feature` scenarios are outside this issue's prompt, abort, readiness, role-isolation, and interaction
scope.
