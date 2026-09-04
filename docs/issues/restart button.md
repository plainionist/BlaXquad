---
title: restart button
priority: 2
---

add a "headquarter" tool box above the agents for management tools

- add "re-launch" button, use an icon or symbol, add tooltip, should not look like refresh should indicate that it has impact,
  it restarts the squad (all copilot sessions) without restarting the shell app

## Architectural diagnosis

The findings below are manifestations of four architectural shortcomings:

| Root cause | Consequence |
|---|---|
| Lifecycle phase, transition exclusion, and command admission have different owners | `SquadApplication`, `SessionRegistry`, and `SquadViewModel` can disagree about whether the squad is running, stopping, or accepting commands |
| One generation's resources have no single teardown contract | Backend-owned sessions and client state, observer tasks, registry entries, ViewModel state, and handoff activity can be stopped or rolled back in an invalid order |
| Session identity is lost across asynchronous boundaries | Work validated before an `await` or channel enqueue can mutate a newer generation at its later commit point |
| Irreversible effects occur before acknowledgement or durable retry ownership | Failed aborts expose uncertain sessions, handoffs become stranded, and rejected prompts lose user input |

These concerns should not be fixed with more independent booleans, catches, timeouts, or UI heuristics. The implementation
needs the following cohesive owners and contracts.

### One authoritative lifecycle owner

Refactor `SessionRegistry` into the single squad-lifecycle authority, renaming it if necessary to reflect that
responsibility. It should own:

- the `Created`, `Starting`, `Running`, `Relaunching`, `Failed`, `Stopping`, and `Stopped` phases;
- the current generation number and role-to-session leases;
- asynchronous transition exclusion;
- external command admission and draining;
- generation-scoped role availability barriers; and
- the capability snapshot used by the protocol.

`SquadApplication.myLifecycleLock`, `SquadViewModel` admission flags, and independently derived availability must be
removed rather than synchronized after the fact. Beginning a transition must atomically leave `Running`, close command
admission, cancel admitted-command retirement tokens, and publish the new capability state. Committing or failing the
transition must update generation and phase while the same transition ownership is still held.

Use an explicit transition handle so every successful `Begin` has exactly one `Commit` or `Fail`. An immediate
`RequestStop` operation may close admission and signal cancellation before shutdown acquires transition ownership, but
it must not inspect, invoke, register, or dispose sessions.

The authoritative state machine should be:

```text
Created  -> Starting    -> Running
                     \-> Failed
Running  -> Relaunching -> Running
                     \-> Failed
Failed   -> Relaunching -> Running or Failed
any      -> Stopping    -> Stopped
```

`Failed` must permit another relaunch when no session generation remains; otherwise one transient restart failure
permanently defeats the feature's purpose.

### One generation teardown contract

The backend remains the sole owner responsible for stopping its sessions and shared client. Expose an explicit reusable
backend stop operation or a backend-owned runtime-generation handle; lifecycle code must never dispose individual
sessions behind the backend's back.

`SessionGeneration` should own its observer cancellation sources and tasks behind one teardown operation. Its individual
cancel, drain, and cancellation-disposal primitives should not remain callable in arbitrary order. The teardown order is
defined by resource dependencies:

1. leave `Running`, close admission, and cancel retirement tokens;
2. stop the handoff producer;
3. drain admitted external commands;
4. cancel event observers;
5. stop the generation through the backend owner, resolving session completion;
6. drain observer tasks;
7. dispose observer cancellation sources and retire generation state.

Teardown must collect failures without skipping later cleanup. Observer draining before backend shutdown is invalid
because `ObserveSessionAsync` waits for session completion. A timeout or catch around that drain would hide the ownership
cycle instead of fixing it.

Everything after a lifecycle `Begin` belongs inside one failure-atomic transaction. A partially started replacement must
be stopped through the backend, drained, removed from the registry and ViewModel, and transitioned to `Failed` even when
an earlier teardown step also fails.

### Generation leases at every asynchronous commit point

Registration should mint a `SessionLease` containing generation, role, and session identity. Carry that lease through:

- session event observation;
- session completion and failure reporting;
- readiness probes;
- prompt, interaction, and abort command admission; and
- every queued ViewModel mutation originating from a session.

The serialized mutation itself must call `IsCurrent(lease)` immediately before changing role, transcript, interaction,
or failure state. A check before an `await`, callback, or channel enqueue is insufficient. Probe-local generation
counters can supplement this check but cannot replace squad-generation and session identity.

Role-only event APIs must be limited to events that genuinely have no session origin. This rule prevents stale-session
races from reappearing one asynchronous call site at a time.

### Command leases and coherent failure semantics

External commands should acquire an admission lease atomically from the lifecycle owner before mutating any domain
state. The lease identifies the exact session generation, contributes to transition draining, and carries a retirement
token canceled when that generation leaves `Running`.

An abort may invalidate its leased role only after admission. Successful backend cancellation clears pending
interactions and restores idle state. If cancellation fails, keep that exact generation and role unavailable until an
abort retry succeeds, the session terminates, or relaunch retires the generation. Do not mark it idle or remove the
barrier merely because the RPC returned an exception.

### Handoff notification as retryable state

The handoff pump is a participant in session lifecycle, not an unrelated background service. Stop it before retiring a
generation, recover pending inbox work against replacement sessions, then resume it before committing `Running`.

Inbox artifacts are already the durable source of pending work. Normal pump iterations should derive required wake-ups
from that inbox state and retry failed notifications. Advance an idempotent per-role/per-generation notification
watermark only after the replacement session accepts the wake-up; reset that watermark for a new generation. A
one-shot recovery loop that logs and suppresses its final failure is not recovery ownership.

### Typed protocol admission and acknowledgement

C# must publish authoritative lifecycle phase and per-role capabilities such as `canSendPrompt` and `canAbort`; Vue
must render those values without recomputing lifecycle rules. Server-side admission remains mandatory because a UI
snapshot can become stale.

Commands that mutate transient UI state need a typed, correlated acknowledgement. In particular, `prompt.send` should
include a request ID, and Vue should clear the draft only after an accepted acknowledgement. Rejection retains the draft
and surfaces the server reason. Disabling controls without acknowledgement cannot close the snapshot race, while
server-side rejection alone cannot prevent client-side data loss.

### Required transition boundaries

| Transition | Required order |
|---|---|
| Startup | Begin `Starting`; prepare workspace/window/backend; start and register leased sessions; finish window session startup; recover and start handoffs; commit `Running` and publish capabilities; release transition |
| Relaunch | Begin `Relaunching` and publish unavailable capabilities; stop handoffs; drain commands; teardown old generation; reset generation-owned UI state; start replacement; recover and restart handoffs; commit `Running`; release transition |
| Relaunch failure | Stop partial replacement through backend; drain its observers; clear its interactions and transient role state; fail generation and publish error/capabilities; release transition while aggregating cleanup failures |
| Shutdown | Request stop and close admission; acquire lifecycle transition; stop handoffs; drain commands; teardown current generation; commit `Stopped`; release transition; dispose non-session resources in reverse ownership order |

## Re-review findings

The goal is not yet achieved. The current implementation still lacks failure-atomic lifecycle transitions in several
paths, and some asynchronous operations lose the session-generation or command-admission identity needed at their
commit point. The fixes must establish the invariants below rather than suppressing individual exceptions or adding
presentation-only guards.

- [x] **High: Relaunch rollback can wedge or deadlock the application.**

  **Root cause:** The relaunch failure path does not own teardown as one failure-atomic transaction. After replacement
  startup succeeds, each `ObserveSessionAsync` task waits for `session.Completion`. On a later handoff recovery/start
  failure, the catch path cancels only event-stream tokens and then calls `SessionGeneration.DrainObserversAsync` before
  disposing the replacement sessions. The drain cannot finish because session completion depends on disposal, while
  normal cleanup cannot acquire the lifecycle semaphore held by relaunch. Other rollback gaps compound this:

  - a fault from the first old-generation drain is immediately re-awaited in the catch path, preventing the remaining
    rollback;
  - failures after `BeginRelaunch` but before the nested `try` never execute rollback;
  - directly disposing sessions bypasses the backend that owns the shared SDK client, so moving only that loop would
    still leak backend resources.

  **Required invariant:** Every path after `BeginRelaunch` must end in exactly one coherent committed or failed state.
  On failure, teardown the replacement generation through its authoritative backend owner before draining observers,
  aggregate cleanup failures without skipping registry/ViewModel/current-generation finalization, and always release
  lifecycle ownership. A timeout, another catch around `Task.WhenAll`, or moving per-session disposal only masks the
  dependency cycle.

- [x] **High: Shutdown is not serialized with startup and relaunch at their mutation boundaries.**

  **Root cause:** Cleanup publishes `Stopping` and runs `SquadViewModel.StopAsync` before acquiring the lifecycle
  coordinator. `StopAsync` snapshots sessions and invokes session methods while startup or relaunch can still register,
  replace, or dispose those same sessions. Checking `Completion.IsCompleted` cannot prevent disposal between the check
  and call. At the opposite boundary, generation completion can be committed after lifecycle ownership is released, so
  shutdown can change the registry to `Stopping` between resource completion and `CompleteGeneration`.

  **Required invariant:** Use one linearization boundary for registry phase and all session-affecting resource mutation.
  A stop request may atomically close admission immediately, but shutdown must acquire lifecycle ownership before
  touching sessions, and generation success or failure must be committed while that same ownership is held. Catching
  `ObjectDisposedException` or moving only one `CompleteGeneration` call leaves the opposing race intact.

- [x] **Medium: A retired readiness probe can mutate replacement role state.**

  **Root cause:** `SquadViewModel.GetRoleReadinessAsync` captures a session, awaits its readiness probe, then queues the
  result through role-only `EnqueueEventAsync`. The eventual mutation checks a probe-local readiness generation but not
  the originating session or squad generation. Readiness-generation numbers are not guaranteed to be globally unique
  across session instances, so a delayed result from the retired session can be accepted by the replacement role.

  **Required invariant:** Capture a lease containing squad generation and session identity before awaiting the probe,
  carry that identity into the queued work, and validate it at the role-state mutation point. Comparing only
  probe-local counters or checking session identity before the await is another time-of-check/time-of-use symptom patch.

- [x] **Medium: A failed abort incorrectly re-enables an un-cancelled role.**

  **Root cause:** `AbortRoleAsync` clears pending interactions and marks the role idle in `finally`, even when the backend
  abort RPC throws. Its caller then removes event suppression, and no failed-abort barrier remains. An abort exception
  does not prove that the original backend operation stopped, so another prompt can be admitted while the prior response
  is still running.

  **Required invariant:** An admitted abort that fails must leave the exact session generation unavailable until a retry
  succeeds, the session terminates, or relaunch replaces it. That failure marker must be generation-scoped so it cannot
  poison replacement sessions. Displaying an error or only marking the role non-working would not prevent overlapping
  commands.

- [x] **High: Handoff notification failures are treated as successful recovery.**

  **Root cause:** Handoff delivery writes the inbox artifact and moves the source to `sent` before notification.
  Notification failures are only logged. Relaunch recovery scans pending inboxes but also logs and suppresses wake-up
  failures, after which normal polling watches outboxes rather than retrying inbox notifications. A single transient
  send failure can therefore leave delivered work indefinitely stranded while relaunch commits as healthy.

  **Required invariant:** A delivered handoff must have durable, retryable notification state, or incomplete recovery
  must prevent the new generation from being committed as healthy. Notification retries must be idempotent and continue
  until the replacement session accepts the wake-up. A one-shot `RecoverAsync` call only patches the common path.

- [x] **Medium: A lifecycle-rejected prompt is irreversibly cleared by the UI.**

  **Root cause:** Lifecycle availability disables only the relaunch control; role prompt controls remain enabled.
  `useDashboardSession.sendPrompt` clears the draft immediately after a fire-and-forget bridge send. If C# rejects the
  command during startup or relaunch, the protocol reports an error but has no correlated command acknowledgement with
  which the UI could retain or restore the rejected draft.

  **Required invariant:** Rejection at authoritative command admission must be side-effect free for both domain and
  user-visible state. Preserve a draft until correlated admission/success is acknowledged, and project lifecycle command
  availability onto all affected role controls while retaining the server-side guard. Disabling controls alone cannot
  close the snapshot race; server rejection alone cannot prevent client-side data loss.

## Latest re-review findings

The earlier findings have materially progressed, but the root-cause design is not complete. The remaining gaps share
three architectural causes:

1. cancellation is still treated as proof that command or runtime ownership has ended;
2. teardown is still a sequence of fallible statements rather than a mandatory, failure-collecting transaction; and
3. capabilities and recovery acknowledgements still use proxies that are weaker than the authoritative generation and
   transition boundaries.

A fresh review of the current implementation confirms that all findings in this section remain present.

- [ ] **High: Early relaunch failure tears down sessions before admitted commands drain.**

  **Root cause:** `BeginRelaunchAsync` signals command retirement, but the only `DrainCommandsAsync` call occurs after
  cancellation checks and handoff shutdown. If cancellation is observed or handoff shutdown throws first, the catch
  path tears down the generation without draining admitted commands. A retirement token is only a request; it does not
  prove that an admitted prompt, abort, or interaction response has stopped using its leased session.

  **Observable failure:** Backend teardown can dispose an old session while an admitted command still invokes it. A
  late command completion can also mutate role or interaction state after failed-relaunch state was reset and published.

  **Required architectural fix:** Make command retirement/draining a mandatory teardown stage that runs even when prior
  stages fail. Collect the handoff-stop or cancellation failure, conclusively drain or transfer ownership of admitted
  commands, and only then permit backend teardown. Catching disposal exceptions, relying on cooperative cancellation,
  or moving the existing teardown call does not restore ownership ordering.

- [ ] **High: Cooperative command cancellation can hold lifecycle ownership forever.**

  **Root cause:** `SessionRegistry` cancels command tokens and then waits indefinitely for every `CommandLease` to be
  disposed. A backend operation is not required to return merely because its token was canceled, so the lifecycle
  transition has no bounded terminal path when a runtime call hangs.

  **Observable failure:** Relaunch remains in `Relaunching` while holding transition ownership, and shutdown waits behind
  it forever. The shell cannot restart the squad or terminate cleanly.

  **Required architectural fix:** Define a bounded retirement protocol without violating exclusive ownership. First
  request cooperative cancellation; after its deadline, atomically transfer ownership to the backend generation owner
  and abort or force-stop that runtime so commands can no longer access the sessions before teardown proceeds. Timing
  out `Task.WhenAll` and disposing sessions while commands may still run is explicitly not a valid fix.

- [ ] **High: Failed backend teardown abandons uncertain runtime ownership but permits replacement.**

  **Root cause:** `CopilotSdkBackend.StopRuntimeAsync` discards its sessions, client, and force-stop references even when
  session disposal, client stop, force-stop, or client disposal fails. `SessionGeneration` likewise memoizes failed
  teardown and clears its resource lists, while `SquadApplication` removes the current generation and enters a
  retryable `Failed` phase. The system therefore equates "cleanup was attempted" with "the runtime is conclusively
  retired."

  **Observable failure:** A later relaunch may create a second Copilot runtime while sessions or the process from the
  failed teardown remain alive. No owner retains a handle with which final cleanup can recover or escalate.

  **Required architectural fix:** Preserve an explicit backend runtime-generation handle and failed-stop state until
  termination is confirmed. Retry or escalate teardown through that owner; if conclusive retirement cannot be
  established, keep lifecycle state non-relaunchable rather than overlapping generations. References and generation
  ownership may be cleared only after confirmed retirement. Suppressing the aggregate or unconditionally nulling state
  is a symptom patch.

- [ ] **High: Handoff notification acknowledgement is not generation-aware.**

  **Root cause:** The handoff notification watermark is keyed by role and pending-work token but not by session
  generation. Relaunch recovery force-bypasses that watermark once. If the replacement notification fails, the prior
  generation's successful watermark remains authoritative, so normal polling sees the same token as already notified
  and never retries it.

  **Observable failure:** Pending inbox work can remain indefinitely unwoken in the replacement session while relaunch
  reports success.

  **Required architectural fix:** Notification obligations and acknowledgements must include the target session
  generation. Starting a generation rearms pending inbox work for that generation, and only that generation accepting
  the wake-up may advance its watermark. A failed recovery attempt must remain visible to ordinary polling until it
  succeeds, or prevent healthy commit. A `force` flag or bounded recovery retry remains a one-shot bypass, not durable
  retry ownership.

- [ ] **High: Active batch handoffs are outside the recovery model.**

  **Root cause:** Pending-notification discovery scans only top-level files in `inbox/in_process`, while batch mode moves
  active handoffs into `inbox/in_process/batch_*` directories. Recovery models one filesystem shape instead of the full
  authoritative handoff queue state machine.

  **Observable failure:** Relaunch during an active batch creates no notification obligation, so neither recovery nor
  normal polling wakes the replacement agent and the whole batch is stranded.

  **Required architectural fix:** Derive notification obligations from every supported pending queue state: top-level
  task entries and all active batch directories. Produce stable, idempotent tokens for the represented work and feed
  them through the same generation-aware acknowledgement mechanism. Adding another top-level wildcard only patches the
  currently observed shape.

- [ ] **Medium: Relaunch capability is published before transition exclusion is released.**

  **Root cause:** `CanRelaunch` treats a finalized transition as available even though its transition semaphore is still
  held until `SquadLifecycleTransition.DisposeAsync`. Startup and relaunch publish snapshots after `Commit` but before
  leaving that scope. `IsFinalized` is therefore a second proxy for admission rather than the actual admission boundary.

  **Observable failure:** The UI can enable relaunch and immediately receive "Another squad lifecycle transition is in
  progress." The same race affects retry after a failed relaunch.

  **Required architectural fix:** Keep capability false until transition ownership is actually released, then publish
  the capability snapshot from that same linearization point. Capability must be derived from the authoritative
  transition/admission state, not an independent finalized flag. Client retries or another Vue transition flag would
  duplicate authority rather than fix it.

- [ ] **Medium: The global abort shortcut bypasses authoritative abort capability.**

  **Root cause:** Capability projection was wired into the visible `PromptComposer` Cancel button but not into the
  command action used by every dispatch path. `useFocusedRoleAbort` retains only the last focused role and invokes
  `cancelRole` unconditionally on double Escape. `cancelRole` sends `role.abort` without consulting that role's current
  C#-published `canAbort` value.

  **Observable failure:** During startup, relaunch, failure, or shutdown, the UI shows abort as unavailable but double
  Escape still sends it. C# correctly rejects the command and the host surfaces a protocol error for an action the UI
  should not have dispatched.

  **Required architectural fix:** Centralize UI abort dispatch behind an action that consumes the current authoritative
  role capability at invocation time, and route both the button and keyboard shortcut through it. Keep server-side
  admission as the final authority for snapshot races. Disabling only the visible button, swallowing the protocol error,
  or deriving eligibility from role status in Vue leaves the bypass or duplicates the rule.

## Definition of done

- [ ] Every begun lifecycle transition reaches exactly one committed or failed terminal state and always releases
  transition ownership, including cancellation and teardown-failure paths.
- [ ] Phase, current generation, session registration, command admission, ViewModel generation reset, and handoff
  lifecycle are coordinated by one authoritative owner rather than separate locks and flags.
- [x] No code outside the backend disposes backend-owned sessions or client resources.
- [ ] Backend shutdown conclusively retires runtime ownership and resolves session completion before observers for that
  generation are drained.
- [x] Every session-derived asynchronous mutation carries a lease and revalidates generation plus session identity at
  the mutation point.
- [x] A rejected command mutates neither domain state nor user-visible transient state.
- [x] A failed abort keeps only its originating generation unavailable until recovery, termination, or replacement.
- [ ] Failure of a partially started generation drains admitted commands, conclusively retires its backend runtime, and
  clears its pending interactions and transient role state before
  publishing the terminal error.
- [ ] Failed handoff notifications for every supported queue state remain under a generation-aware, idempotent retry
  path across relaunch.
- [ ] Protocol capabilities use the same released-transition admission boundary as C#, all affected controls and
  command shortcuts consume them, and prompt drafts
  survive rejected sends.
- [ ] A failed relaunch leaves the shell usable and permits a later relaunch attempt only after the failed runtime is
  conclusively retired.
- [x] Snapshot creation never waits on the asynchronous transition lock.

## Symptom patches to reject

- timeouts or exception suppression around observer drains;
- direct per-session disposal outside the backend;
- additional pre-enqueue generation checks without commit-time validation;
- globally unique readiness counters without session identity;
- UI-only lifecycle flags or controls derived from role status;
- clearing or restoring drafts without correlated server acknowledgement;
- marking a role idle after a failed abort;
- bounded one-shot handoff notification retries; and
- another lifecycle wrapper that merely forwards to the existing independent lock, registry, and ViewModel flags.
