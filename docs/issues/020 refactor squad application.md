---
title: Refactor SquadApplication lifecycle responsibilities
priority: 20
---

# Refactor SquadApplication lifecycle responsibilities

`SquadApplication` combines process lifetime, startup preparation, session-generation management, event observation,
relaunch transactions, teardown ordering, and cleanup error aggregation.

## Responsibilities currently combined

- waiting for host, window, backend, handoff, and cancellation terminal signals;
- starting process-wide resources such as the sleep inhibitor and window;
- creating, registering, observing, replacing, and tearing down session generations;
- coordinating the handoff pump with generation changes; and
- collecting and rethrowing cleanup failures.

## Refactoring direction

Keep `SquadApplication` responsible for the process run loop and process-wide resource lifetime. Introduce one
generation-lifecycle owner that encapsulates:

- session generation creation and registration;
- session event/completion observer ownership;
- backend and handoff startup/recovery for a generation;
- relaunch replacement; and
- ordered, failure-collecting generation teardown.

Build on the backend runtime-generation handle and authoritative lifecycle transition introduced by the restart
architecture rather than adding another parallel lifecycle state machine. Those contracts are not present on `main`;
the abandoned `SessionGeneration` and `SquadLifecycleTransition` names are not assumed to exist. Startup, relaunch,
failure rollback, and shutdown must use the accepted lifecycle aggregate as the authoritative phase and
command-admission owner.

## Coordination status

Slice 1 is complete (`853c6bc636`). Slices 2 and 3 remain blocked until:

- restart architecture Slice 2 provides a backend-owned runtime-generation handle with confirmed/uncertain retirement;
  and
- restart architecture Slice 3 provides the authoritative lifecycle aggregate, generation/session leases, command
  retirement, and transition handle.

Do not emulate either missing prerequisite inside this issue. In particular, do not add lifecycle flags or locks to
`SquadApplication`, expose unleased sessions, or move session disposal into a new headquarters wrapper.

## Implementation plan

### Slice 1: Characterize generation lifecycle ordering

**Status: complete (853c6bc636)**

1. Extend the existing black-box `SquadApplication` acceptance support with one shared lifecycle trace used by the
   recording window, backend, sessions, handoff pump, and process-wide resources. Keep the trace in test support; add no
   production instrumentation or lifecycle API.
2. Add a healthy startup/shutdown scenario that asserts only ownership-significant boundaries:
   - the process-wide window is started before backend generation startup;
   - session registration completes before the window is told that sessions started;
   - handoff recovery and production begin only after sessions are available;
   - shutdown stops handoff production before retiring the backend generation;
   - backend-owned session completion is resolved before observer retirement completes; and
   - generation teardown finishes before the window and remaining process-wide resources are released.
3. Add a partial-start failure scenario that records a registered session and a later startup failure, then proves that
   rollback continues through generation and process-wide cleanup and reports both the primary and cleanup failures.
   Reuse the existing aggregate-failure assertions rather than introducing an implementation-specific exception shape.
4. Do not freeze incidental ordering among independent process-wide disposals, private method names, collection shapes,
   or current direct session-disposal mechanics. The characterization must remain valid when session disposal moves
   behind the backend runtime owner.

Slice 1 is accepted when:

- the Gherkin scenarios fail if handoff, backend generation, observers, and process-wide resources cross the required
  ownership order;
- partial startup rollback proves that one cleanup failure cannot skip later mandatory cleanup;
- the scenarios exercise `SquadApplication` through its public lifecycle rather than testing a proposed extracted type;
  and
- all existing startup, handoff, failure, and shutdown scenarios remain unchanged and green.

#### Review findings on 9b0883b395

Resolved by `853c6bc636`.

**Finding 1 — high**

- **Location:** `src/squad.Specs/StepDefinitions/ViewModelSteps.cs` (`WireLifecycleTrace`),
  `src/squad.Specs/Support/RecordingAgentSession.cs` (`Events` finally / `DisposeAsync`), and
  `Then the lifecycle trace shows session completion resolving before observer retirement completes`.
- **Violated behavior:** Slice 1 is accepted only when the Gherkin fails if observers cross the required ownership
  order: backend-owned session completion resolves before observer retirement completes.
- **Root cause:** `WireLifecycleTrace` sets `IgnoreEventCancellation = true` so `Events()` ignores
  `SquadApplication`'s shutdown-wide `myEventCancellation.Cancel()`, which runs before session disposal. The scenario
  then treats `session.coder.eventsCompleted` (the `Events()` enumerator finishing) as observer retirement. With
  cancellation ignored, that milestone is recorded only after `DisposeAsync` resolves completion and closes the channel,
  so the assertion characterizes the recording session's internal dispose sequence rather than production cleanup. The
  scenario would still pass if production continued to drain event observers before session completion.
- **Required outcome:** Do not suppress session event cancellation to manufacture the order. Use a collaborator-visible
  milestone that current public `SquadApplication` cleanup actually performs and that still holds when session disposal
  moves behind the backend owner. The scenario must fail if observer tasks are drained before session completion
  resolves.

**Finding 2 — high**

- **Location:** `Given a SquadApplication with recording roles {string} and a lifecycle trace whose backend fails
  during startup` and `Then the lifecycle trace shows generation and process-wide cleanup completed despite the cleanup
  failure`.
- **Violated behavior:** Partial-start rollback must prove that one cleanup failure cannot skip later mandatory
  cleanup, including generation teardown and process-wide release after a registered session and a later startup
  failure.
- **Root cause:** The injected cleanup failure is `RecordingHandoffPump.FailOnDispose`. Handoff-pump disposal runs after
  session disposal, backend disposal, window stop, and window disposal. The Then step still asserts those earlier
  steps as evidence they survived the cleanup failure. Only sleep-inhibitor disposal is actually later. The Then also
  asserts only the registered `coder` session was disposed, so the unpublished `reviewer` session from the same
  partial start is not locked in.
- **Required outcome:** Inject the cleanup failure in a generation-scoped teardown step that precedes process-wide
  release (registered session disposal or backend disposal). Assert the later mandatory steps still run, and that every
  session created for the partial start is disposed, including the unpublished one. Keep using the existing aggregate
  `the application lifecycle contains {string} and {string}` assertion.

### Slice 2: Extract one generation runtime

**Status: blocked on the accepted backend runtime and lifecycle contracts**

1. Introduce one `SquadRuntime` (or retain the accepted equivalent name) representing exactly one generation. It owns
   the backend runtime handle, generation/session leases, event and completion observer cancellation sources and tasks,
   and generation-scoped teardown state. Keep it in its own source file.
2. Move session registration and observer creation out of `SquadApplication`. Every observer carries its session lease
   through asynchronous work and relies on the lifecycle/ViewModel commit boundary to reject retired-generation
   mutations; do not add a second currency check or role-only substitute.
3. Give the runtime one idempotent teardown operation. It must execute every mandatory stage, collect failures, stop the
   backend runtime before awaiting observers that depend on session completion, and retain an uncertain backend handle
   when retirement is not confirmed.
4. Remove direct session disposal and generation-scoped session/task/token collections from `SquadApplication`.
   Sessions remain inspectable only through existing typed snapshots or narrowly scoped internal test support; do not
   preserve `SquadApplication.Sessions` as an ownership API.

Slice 2 is accepted when:

- one runtime owns all generation-scoped sessions and observers while the backend handle remains the sole disposer of
  backend sessions;
- teardown is retry-safe, failure-collecting, and cannot drain completion observers before backend retirement resolves
  session completion;
- partial registration failure leaves no unowned session, observer, cancellation source, or backend handle; and
- the Slice 1 ordering scenarios plus existing session-event, partial-start, cleanup, and shutdown scenarios remain
  green.

### Slice 3: Extract generation orchestration from the shell

**Status: blocked until Slice 2 is accepted**

1. Introduce `SquadRuntimeController` as the sole owner of the current `SquadRuntime`. It coordinates generation
   construction, registration, handoff recovery/start, rollback, replacement, and teardown while consuming the
   authoritative lifecycle transition; it owns no lifecycle phase, command-admission state, window lifetime, host
   lease, workspace preparation, or sleep inhibition.
2. Define one internal generation-construction path and one rollback path. Startup and later relaunch call those same
   paths wherever their behavior is identical. A focused callback may bridge the window's sessions-started notification
   at the required boundary without transferring window ownership to the controller.
3. Reduce `SquadApplication` to process preparation, process-wide resource startup/disposal, the outer terminal-signal
   run loop, and delegation to the controller. Keep primary-versus-cleanup exception aggregation at the owner whose
   resources are being unwound; do not add a pass-through lifecycle facade.
4. Keep transition begin/commit/fail and capability publication in the accepted lifecycle aggregate. The controller
   performs I/O under the transition contract but never duplicates phase or admission state.
5. Update composition in `Launch` and black-box support without widening public construction solely for tests. Add an
   architecture assertion that `SquadApplication` has no generation session, observer, or handoff lifecycle fields.

Slice 3 and this refactoring are accepted when:

- `SquadApplication` owns only the outer run/termination loop and process-wide resources;
- `SquadRuntimeController` owns current-generation orchestration and `SquadRuntime` owns one generation's resources;
- startup, future relaunch, and their failure rollback share generation construction and teardown paths;
- no session is disposed outside its backend runtime owner and no second lifecycle authority exists;
- teardown ordering and aggregate-failure behavior satisfy the Slice 1 characterization; and
- focused startup, handoff, event, failure, command-drain, and shutdown scenarios plus the full acceptance suite pass.

## Acceptance criteria

- `SquadApplication` owns the outer run/termination loop and process-wide resources only.
- One cohesive owner manages generation-scoped backend sessions, observers, and handoff participation.
- Startup and relaunch share the same generation construction and rollback path where their behavior is identical.
- Teardown ordering and aggregate-failure behavior remain explicit and unchanged.
- No session is disposed outside the backend owner, and no second lifecycle authority is introduced.
- Existing black-box Gherkin scenarios for startup, relaunch, failure, and shutdown remain green.

## Why priority 20

Lifecycle mistakes can leak runtimes, deadlock shutdown, or corrupt relaunch state. Extracting generation ownership has
high system-wide benefit, but it should follow the central ViewModel cleanup because the two collaborate closely.