---
title: Restart the active squad
priority: 100
---

# Restart the active squad

## Goal

Allow the operator to replace every agent session without restarting the Headquarters process, window, UI
connection, project lease, control endpoint, or process-owned state.

Expose that operation through two UI actions:

- an explicit **Restart squad** action; and
- the issue explorer's **Play** action, which first replaces the squad and only then prepares the selected issue as
  the replacement leader's draft.

Neither action sends a prompt automatically.

## Current starting point

The squad/member restructuring formerly tracked by issue 024 is complete. This issue must build on the current
implementation rather than introduce another lifecycle model:

- `Headquarters` already owns the process shell, a serialized active-squad slot, and an unused
  `ReplaceSquadAsync` operation.
- `Squad` already owns one complete generation: backend, runtime, sessions, observers, member processors, command
  admission, transient member state, and handoff-pump participation.
- `Squad.RetireAsync` already closes admission before teardown and reports whether retirement was conclusive.
- `SquadViewModel` is already the process-lifetime member UI port. It installs and uninstalls `SquadMembers`, rejects
  commands against a retired generation, and suppresses late publications by generation identity.
- `LaunchPreparer.PrepareGenerationAsync` already reloads configuration and role prompts without resetting
  worktrees or clearing handoff queues.
- `TranscriptStore` already lives for the Headquarters process while each generation receives a revocable handle.

The remaining gaps are concrete:

- nothing invokes `Headquarters.ReplaceSquadAsync`;
- the UI protocol has no process-lifecycle command port, lifecycle snapshot, restart command, start-issue command,
  or success acknowledgement;
- concurrent calls currently wait on the active-slot semaphore instead of rejecting while another replacement is
  in progress;
- `Headquarters.RunAsync` captures fatal backend and handoff tasks from only the initial squad, so failures from a
  replacement would not terminate Headquarters and a late failure from a retired squad could be observed instead;
- `Squad.RetireAsync` permanently caches an inconclusive result even though lower teardown stages support retry;
  and
- Play currently edits and focuses the leader draft entirely in Vue without sending any backend command.

## Ownership and boundaries

Keep the existing lifetime split:

- `Headquarters` remains the sole authority for initial installation, replacement, final stop, replacement phase,
  and replacement capabilities.
- `Squad` remains the cohesive owner of generation startup and retirement. Headquarters must not dismantle backend,
  session, processor, observer, interaction, or handoff-pump resources itself.
- `SquadViewModel` remains a generation-forwarding member facade. Do not add restart or start-issue operations to
  `ISquadUi`, and do not add a `squad.Application` dependency on `squad.Runtime`.
- Add a separate transport-neutral Headquarters UI port in `squad.Ui.Abstractions` for lifecycle snapshots,
  `RestartSquadAsync`, and `StartIssueAsync`. `Headquarters` (or one lifecycle coordinator it directly owns)
  implements that port.
- Pass both the member UI port and Headquarters UI port through `HostingContext` into `UiProtocolSession`. Resolve
  their construction in the composition root with one explicit factory or bind-once seam; the protocol must never
  capture a nullable Headquarters reference.
- `UiCommandHandler` validates wire shape and forwards operations. Headquarters owns replacement admission,
  issue-catalog validation, lifecycle sequencing, and result construction.
- Vue owns only presentation state: pending button state, unsent drafts, focus, and reconciliation of correlated
  responses.

## Authoritative lifecycle state

Publish a `headquarters.snapshot` containing one coherent view of:

- `phase`: `starting`, `running`, `replacing`, `failed`, or `stopping`;
- `generation`: the current opaque generation token, or `null` while the slot is empty;
- `canRestart`; and
- `canStartIssue`.

The exact C# representation is typed; `squad.Ui.Protocol` maps it to the stable lowercase wire values above.
Capabilities are derived from Headquarters state, never from member status in Vue. Restart remains valid while a
member is working because retirement is responsible for closing admission and cancelling that generation.

Both capabilities are false during initial startup, replacement, and process shutdown. After a failed operation,
they become true only when another request can safely retry an empty-slot installation or the unfinished
retirement of the still-owned generation. They remain false when safe in-process recovery is impossible.

Publish lifecycle changes immediately. The server still rechecks admission atomically for every command because a
rendered capability can be stale.

## One replacement operation

Explicit restart and start-issue use one replacement algorithm. A small wrapper may supply optional issue context;
do not duplicate the retirement and installation method merely to vary the post-start result.

For an admitted operation:

1. acquire replacement ownership without waiting behind another user-initiated replacement, and publish
   `replacing` with both capabilities disabled;
2. for start-issue, re-enumerate the authoritative issue catalog and require an exact path match before mutating the
   active squad;
3. retire the current squad through `Squad.RetireAsync`;
4. refuse to create a replacement until retirement is conclusive;
5. call `PrepareGenerationAsync` to reload current configuration, leader selection, role prompts, provider context,
   and workspace-tool configuration;
6. create, install, and start one new `Squad` using the existing `InstallSquadUnlockedAsync` ordering;
7. publish `running` for the new generation; and
8. return the operation result used by the correlated protocol acknowledgement.

`Squad.StartAsync` is the readiness boundary: all configured sessions have been registered, the UI has been
notified, and the replacement handoff pump has started. Do not wait for the leader to become idle before
acknowledging start-issue; the resulting prompt is only an unsent draft.

Starting the replacement pump resumes polling the existing process-scoped handoff files. Replacement must not run
process preparation, reset a worktree, clear a handoff directory, or invent a second queue-recovery mechanism.

## Admission, retirement, and failure

- A second `squad.restart` or `issue.start` received while replacement ownership is held is rejected immediately
  with its own correlated error; it is not queued for a second replacement.
- Final Headquarters shutdown uses the same active-slot gate. Once stopping begins, no new replacement is admitted;
  shutdown either wins admission or waits for the already-admitted replacement to leave the slot consistent.
- One retirement attempt may be shared by concurrent callers, but an inconclusive result must not be cached as the
  squad's permanent result. Preserve successful teardown stages and allow a later request to retry only unfinished
  stages, matching `SessionGeneration.TeardownAsync`'s retry contract.
- An inconclusive retirement keeps the old generation owned and prevents replacement startup. A retry may only
  install a new generation after that same squad reports conclusive retirement.
- If replacement startup fails and partial-generation retirement is conclusive, leave the slot empty and retryable.
  If cleanup is inconclusive, retain that generation and disable further replacement unless its unfinished
  retirement can be retried safely.
- Every rejection or failure leaves the Headquarters shell, window, UI protocol, lease, and control endpoint alive.
  The initiating request receives a correlated `protocol.error`; start-issue produces no success acknowledgement
  and changes no draft.

Make active-generation failure observation replacement-aware. `Headquarters.RunAsync` must wait on a stable
process-lifetime signal or equivalent dynamic monitor, not local copies of the initial squad's `BackendFailure` and
`HandoffFailure` tasks. A fatal failure terminates Headquarters only when it belongs to the currently owned,
non-retiring generation. Fatal failures from a successfully installed replacement retain today's diagnostics;
late signals from retired generations are ignored.

## State boundary

| Preserved across squad replacement | Replaced or cleared |
|---|---|
| Headquarters process, lease, control endpoint, window, UI transport, and sleep inhibition | `Squad` and generation identity |
| Issue catalog and workspace-tool service instances | Provider backend/runtime and every agent session |
| Worktrees, repository contents, and process-scoped handoff files | Member directory, processors, command admission, observers, and cancellation state |
| Process-owned transcript store, visible transcript history, and monotonic transcript sequences | Pending permissions, inputs, elicitations, active operations, readiness, and failure state |
| Existing Vue drafts for unaffected members | The successful start-issue target leader's draft, which is replaced by the acknowledged issue prompt |

Configuration, leader selection, role prompts, provider context, and configured workspace-tool command are reloaded
for the replacement. They are neither copied from the retired squad nor treated as process-constant state.

## Protocol contract

Add these correlated client commands:

| Command | Required request fields | Success response |
|---|---|---|
| `squad.restart` | non-empty `requestId`; no role or payload | `squad.restarted` with the same `requestId` and `{ generation }` |
| `issue.start` | non-empty `requestId`; payload `{ path }` | `issue.started` with the same `requestId` and `{ generation, leader, prompt }` |

`prompt` is built in C# from the catalog's canonical path as `process this issue: "<path>"`. The response reports
the leader loaded for the replacement generation, not the leader visible before replacement. The command does not
call `prompt.send`.

Malformed input, an unknown issue path, busy or stopping admission, inconclusive retirement, and startup failure
use the existing correlated `protocol.error` envelope. No broad acknowledgement retrofit is required for existing
commands.

The opaque generation token in each success response must match the active generation in
`headquarters.snapshot`. Vue applies an `issue.started` result only when both its request ID is still pending and
its generation is current; a late result must not overwrite a draft in a newer generation.

## User interface

- Add a compact restart icon action to the existing toolbar row above the member panels, visually separated from
  issue and workspace tools rather than adding another large section.
- Use an impact-oriented power/restart-session icon, not a refresh icon. Its accessible name is **Restart squad**;
  its tooltip explains that every agent session will be replaced.
- Enable the action only from `canRestart`, and also disable it locally while a restart or start-issue request is
  pending so double clicks cannot emit duplicate commands before the next snapshot arrives.
- Change Play to send `issue.start` with a fresh request ID. Its enabled state comes from `canStartIssue`, not from
  the browser inferring that a currently rendered leader is safe to use.
- Keep browsing, previewing, and copying issues side-effect free.
- On a current `issue.started` response, replace only the acknowledged leader's draft with the acknowledged prompt
  and focus that prompt. Preserve other member drafts.
- On a correlated error, clear local pending state, leave all drafts unchanged, and show the existing protocol error
  presentation. Authoritative capabilities decide whether retry is available.
- Explicit restart never creates, changes, focuses, or sends a prompt.

## Implementation slices

### Slice 1: Finish replacement lifecycle correctness

1. Introduce typed Headquarters phase/capability/result values and make user replacement admission non-queuing.
2. Make inconclusive `Squad` retirement retry only unfinished teardown while continuing to block overlap.
3. Replace the initial-generation fatal-task capture in `Headquarters.RunAsync` with generation-aware monitoring.
4. Cover explicit replacement, concurrent rejection, retryable and non-retryable failure, shutdown races, and
   replacement-generation terminal failures through the real stdio-hosted Gherkin suite and fake provider.

### Slice 2: Expose process commands

1. Add the separate Headquarters UI port and carry it through `HostingContext`, both hosting adapters, and
   `UiProtocolSession` without changing the `squad.Application` dependency direction.
2. Publish `headquarters.snapshot` on initial UI readiness and every lifecycle transition.
3. Add `squad.restart`, `squad.restarted`, validation, request correlation, and correlated errors.
4. Prove fresh session IDs, exact disposal/start counts, preserved Headquarters control, preserved transcript and
   handoff state, cleared interactions, and stale-generation suppression through Gherkin scenarios.

### Slice 3: Make Play start an issue

1. Add `issue.start` as a Headquarters-owned operation that validates a freshly enumerated catalog before
   retirement and then invokes the same replacement core with issue context.
2. Return the replacement generation, reloaded leader, and server-built prompt in `issue.started`; never send it to
   the provider.
3. Cover invalid paths, catalog I/O failure, replacement failure, changed configuration/leader, repeated selection
   of the same issue, and absence of `prompt.send` through Gherkin scenarios.

### Slice 4: Add the controls and reconciliation

1. Consume `headquarters.snapshot` in the dashboard session and expose lifecycle capabilities and pending actions.
2. Add the restart action and route Issue Explorer Play through `issue.start`.
3. Apply only the current correlated acknowledgement and use its leader and prompt when staging the draft.
4. Update the existing issue-explorer Playwright specs and add focused restart specs for accessibility, placement,
   capability and pending states, success, failure, stale acknowledgements, draft preservation, and focus.

## Acceptance criteria

- Explicit restart replaces every configured member session with a fresh session ID exactly once while the same
  Headquarters process, lease, control endpoint, window, and UI connection remain usable.
- Playing an issue validates its current catalog path, replaces the squad, and only after successful startup stages
  the acknowledged prompt for the replacement configuration's leader.
- Playing the same issue again performs another replacement; browsing, previewing, and copying never do.
- Restart and start-issue use one serialized C# replacement algorithm and reject concurrent user replacements
  rather than queueing them.
- Replacement and final shutdown cannot install, retire, or dispose the same generation concurrently.
- No replacement starts over an inconclusively retired generation; a retry continues that owned retirement and
  installs only after it becomes conclusive.
- A retryable startup failure leaves an empty slot; a cleanup state that cannot be retried safely disables both
  capabilities while Headquarters remains responsive for diagnostics and shutdown.
- Current configuration, leader, role prompts, provider context, and workspace-tool configuration are loaded for
  every replacement without rerunning process preparation.
- Worktrees, repository content, handoff files, transcript history, and transcript sequence continuity survive;
  pending interactions and all other generation-owned state do not.
- A fatal backend or handoff-pump failure from the replacement generation is observed by Headquarters, while late
  signals, events, completions, readiness results, and publications from a retired generation have no effect.
- `headquarters.snapshot` is authoritative for phase and capabilities. Every replacement command is correlated,
  every success identifies its generation, and every rejection or failure correlates to the initiating request.
- An invalid or failed `issue.start` leaves every draft unchanged. A successful one changes only the acknowledged
  leader's draft and never sends it automatically.
- Backend behavior is covered only through the existing black-box Gherkin suite and frontend behavior through
  focused Playwright specs using shared support.

## Non-goals

- Redesigning the completed Headquarters, squad, member, or role ownership model.
- Restarting one member independently or running old and replacement generations concurrently.
- Restarting the Headquarters process, window, control endpoint, hosting adapter, or UI protocol session.
- Resetting worktrees or clearing process-scoped handoff files, transcript history, or unrelated Vue drafts.
- Persisting a "current issue" as new backend state.
- Automatically sending the prepared issue prompt.
- Adding acknowledgements to unrelated existing protocol commands.