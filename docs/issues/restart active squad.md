---
title: Restart the active squad
priority: 100
---

# Restart the active squad

## Goal

Allow Headquarters to replace the active squad without restarting the Headquarters process, window, control endpoint,
or other process-wide services.

The same operation has two entry points:

- an explicit restart action in the Headquarters toolbox; and
- an implicit restart whenever the user starts an issue from the issue explorer.

Both entry points create fresh agent sessions for every squad member. Starting an issue prepares that issue for the
leader member only after the replacement squad is running.

## Architectural prerequisite

Implement this feature on the domain model from
[issue 024](024%20establish%20squad-member-centered%20application%20domain%20model.md). Do not rebuild the reverted
restart attempt or add lifecycle flags around the current collection of process-owned resources.

The required boundary is:

- Headquarters owns the process shell, project lease, control endpoint, window and UI transport, issue catalog,
  durable workspace services, and one active-squad slot.
- One `Squad` owns a complete replaceable generation: backend runtime, member sessions, command admission, observers,
  handoff-pump participation, member processors, and transient member state.
- The active-squad slot serializes start, replacement, and stop. A squad exposes one cohesive retirement operation;
  Headquarters does not dismantle its resources individually.

Issue 024 owns that restructuring. This issue adds commands and presentation to the resulting replacement operation.

## Replacement semantics

Restart replaces the complete active squad generation:

1. acquire exclusive replacement ownership and publish unavailable command capabilities;
2. close admission and retire the current squad through its single retirement contract;
3. refuse to continue if the old generation cannot be conclusively retired;
4. load the current squad configuration and role prompts;
5. create and start a new squad generation and all configured member sessions;
6. recover durable pending handoffs for the new generation;
7. install and publish the new squad as active; and
8. acknowledge the initiating request.

No old and replacement squad generation may be active at the same time. Events, readiness results, command
completions, and interaction responses from the retired generation must be rejected at their mutation or publication
boundary.

If retirement or replacement startup fails, Headquarters remains alive and publishes the failure. Retry is available
only when no uncertain old runtime can overlap the next generation. Headquarters shutdown uses the same serialized
ownership boundary and cannot race a restart.

## State boundary

Restart preserves process and durable workspace state while replacing generation state.

| Preserved across restart | Replaced or cleared |
|---|---|
| Headquarters process and project lease | Active `Squad` instance and generation identity |
| Window, UI transport, and issue catalog | Backend runtime and all member agent sessions |
| Worktrees and repository contents | Command admission, observers, and cancellation state |
| Durable handoff queues | Pending permissions, inputs, and elicitations |
| Archived and visible transcript history | Working, tool, readiness, and failure state from retired sessions |
| User-selected issue path after a successful issue start | Any uncommitted publication from the retired generation |

Transcript history remains visible but is not shared as mutable state between generations. Durable handoffs are
recovered and their wake-up acknowledgements are scoped to the replacement generation.

## Explicit restart

Add a compact Headquarters toolbox above the member panels with a restart action that:

- uses an impact-oriented icon rather than a refresh icon;
- has an accessible label and tooltip explaining that every agent session will be replaced;
- is enabled only when the C#-published `canRestart` capability is true;
- sends one typed, correlated restart command; and
- reports rejection or failure without closing the Headquarters shell.

The button does not orchestrate teardown or startup in Vue. It invokes the authoritative Headquarters replacement
operation and waits for its acknowledgement.

## Starting an issue

The issue explorer's play action becomes the authoritative start-issue action. Opening the catalog, previewing an
issue, or copying its path does not restart the squad.

Starting an issue must:

1. send one typed, correlated command containing the selected issue path;
2. validate that the path identifies an issue from the authoritative workspace catalog;
3. invoke the same squad-replacement operation as explicit restart;
4. wait until the replacement squad and leader member are ready;
5. prepare `process this issue: "<path>"` as the leader member's prompt; and
6. acknowledge success so the UI can focus that prompt.

The prompt is not prepared for the retiring squad and is not sent automatically. A failed or rejected replacement
does not stage the issue prompt or report that the issue started. Every successful start-issue invocation creates a
new squad generation, even when the selected path matches the previous issue.

This sequence is one C#-owned application operation rather than a Vue chain of `restart` followed by `prompt.update`.
That keeps the issue boundary authoritative and prevents a stale acknowledgement or concurrent restart from targeting
the wrong generation.

## Protocol and capabilities

C# publishes the active-squad phase and authoritative capabilities such as `canRestart` and `canStartIssue`. Vue
renders those values and does not infer lifecycle rules from member status.

Restart and start-issue commands carry request IDs and return typed success or rejection acknowledgements. The
acknowledgement identifies the resulting squad generation so late responses cannot be applied to a newer squad.
Server-side admission remains authoritative when a UI snapshot is stale.

All dispatch paths for an operation consume the same current capability. Keyboard shortcuts, if added, must call the
same action as the visible control rather than sending protocol messages directly.

## Implementation plan

### Slice 1: Protect replacement behavior

1. Add black-box Gherkin scenarios that observe Headquarters and agent-session identities before and after a
   replacement.
2. Cover explicit restart, start-issue restart, concurrent replacement rejection, shutdown during replacement,
   replacement startup failure, uncertain retirement, and stale-generation events.
3. Cover the state boundary: transcript history and durable handoffs survive, pending interactions and transient
   member state do not, and current configuration and role prompts are reloaded.

### Slice 2: Expose typed application commands

1. Expose the issue-024 active-squad replacement operation through the UI application port.
2. Add typed restart and start-issue requests, correlated acknowledgements, active-squad phase, and capabilities.
3. Make start-issue validate the issue, replace the squad, and prepare the leader prompt as one application operation.
4. Keep rejection side-effect free and retain Headquarters usability after a failed replacement.

### Slice 3: Add the frontend controls

1. Add the Headquarters toolbox and explicit restart control.
2. Route the issue explorer's play action through start-issue instead of constructing a leader prompt locally.
3. Drive enabled states from published capabilities and apply acknowledgements only to their originating request and
   resulting generation.
4. Add focused Playwright coverage for button placement, tooltip and accessible name, disabled states, successful
   restart, failed restart, and issue prompt preparation after replacement.

## Acceptance criteria

- Explicit restart replaces every configured member session exactly once without restarting Headquarters, its
  window, control endpoint, project lease, or UI connection.
- Starting any issue replaces the active squad before preparing the leader member's issue prompt.
- Browsing, previewing, or copying an issue does not restart the squad.
- Explicit restart and start-issue use the same serialized C# replacement operation.
- A concurrent restart or start-issue request is rejected without creating an overlapping generation.
- Headquarters shutdown and squad replacement cannot mutate or dispose the same generation concurrently.
- A replacement does not start while retirement of the prior backend runtime is uncertain.
- Failure leaves Headquarters responsive and permits retry only after prior runtime retirement is confirmed.
- Events and completions from a retired generation cannot mutate or publish into the replacement generation.
- Current configuration and role prompts are loaded for the replacement squad.
- Transcript history, worktrees, and durable handoff queues survive; pending interactions and transient member state
  from retired sessions are cleared.
- Pending handoffs are retried against and acknowledged by the replacement generation.
- Restart and start-issue controls reflect C#-published capabilities and use correlated acknowledgements.
- A failed start-issue does not stage or send the leader prompt.
- Backend behavior is covered through the existing black-box Gherkin suite and frontend behavior through focused
  Playwright specs using shared test support.

## Non-goals

- Redesigning the Headquarters, squad, member, or role ownership model in this feature.
- Restarting one member independently.
- Running old and replacement squads concurrently.
- Restarting the Headquarters process, window, or control endpoint.
- Clearing worktrees, durable handoff queues, or transcript history.
- Automatically sending the prepared issue prompt.
- Restoring code or architecture from the reverted restart attempt.