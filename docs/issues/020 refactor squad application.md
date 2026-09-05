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

This issue is self-contained. Introduce the focused backend runtime-generation and lifecycle-transition contracts it
needs rather than waiting on or modifying another issue. Those contracts must remain limited to separating
`SquadApplication` responsibilities; do not add relaunch presentation or another parallel lifecycle state machine.
Startup, future relaunch, failure rollback, and shutdown must use `SessionRegistry` as the authoritative phase and
command-admission owner.

## Coordination status

Slice 1 is complete (`853c6bc636`). Slice 2 is complete (`3f0feed336`). Slices 3 and 4 remain blocked until the
architect authorizes the next slice.

Introduce the missing focused contracts within this issue. Do not add lifecycle flags or locks to `SquadApplication`,
expose unleased sessions, or move session disposal into a headquarters wrapper that bypasses backend ownership.

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

### Slice 2: Establish backend-owned generation resources

**Status: complete (3f0feed336)**

1. Add an `IAgentRuntime` contract in the agent-provider abstraction assembly, in its own source file. An
   `IAgentBackend` creates the runtime handle before fallible session startup; the runtime then starts and registers its
   sessions. The runtime exclusively owns its provider client and every session it creates.
2. Implement the runtime handle in the Copilot SDK adapter and recording acceptance support. Move all session disposal,
   shared-client stop/disposal, and partial-start rollback behind that handle. A failed stop must retain enough owned
   state for a later retry; cleanup attempted is not equivalent to ownership retired.
3. Introduce `SessionGeneration` in headquarters as the owner of one runtime handle plus its registered-session
   projection, event/completion observer tasks, and observer cancellation sources. Move registration and observer
   creation out of `SquadApplication` without changing event ordering or failure reporting.
4. Give `SessionGeneration` one idempotent, failure-collecting teardown operation. It cancels event observation, asks
   the runtime owner to resolve session completion and retire its resources, then drains observers and disposes their
   cancellation sources. It never calls `IAgentSession.DisposeAsync`.
5. Keep `SquadApplication` responsible for invoking generation teardown in the existing handoff/command/process cleanup
   order for this slice. Remove its direct session-disposal loop and its generation-scoped session, observer-task, and
   cancellation-source collections. Do not introduce lifecycle phases, relaunch behavior, or UI/protocol changes yet.

Slice 2 is accepted when:

- `IAgentRuntime` is the sole disposer of backend sessions and provider-client resources, including partial startup;
- `SessionGeneration` is the sole headquarters owner of registered-session projections and observer resources;
- teardown is retry-safe, failure-collecting, and cannot drain completion observers before backend retirement resolves
  session completion;
- partial registration failure leaves no unowned session, observer, cancellation source, client, or runtime handle;
- `SquadApplication` contains no session-disposal loop or generation-scoped observer collections; and
- the Slice 1 ordering scenarios plus existing session-event, partial-start, cleanup, and shutdown scenarios remain
  green.

#### Review findings on adcc41b011

Resolved by `3bc12c16c4` except as restated below.

**Finding 1 — high**

- **Location:** `src/squad.CopilotSdk/CopilotSdkAgentRuntime.cs` (`DisposeAsync`),
  `src/squad.Specs/Support/RecordingAgentRuntime.cs` (`DisposeAsync`), and
  `src/squad-hq/Commands/SessionGeneration.cs` (`TeardownAsync` / `TeardownCoreAsync`).
- **Violated behavior:** Slice 2 requires a failed stop to retain owned state for a later retry. Cleanup attempted is
  not ownership retired. Teardown must be retry-safe and failure-collecting.
- **Root cause:** Both runtime implementations set `myDisposed = true` before stop/disposal finishes, then clear the
  session list and return immediately on a later `DisposeAsync`. `CopilotSdkAgentRuntime` still attempts client
  stop/dispose after that flag, but a retry is skipped entirely. `SessionGeneration.TeardownAsync` caches the first
  teardown task with `myTeardown ??=`, so a failed runtime dispose is never retried, and `TeardownCoreAsync` still
  clears observer collections and disposes cancellation sources after that failure.
- **Required outcome:** Keep the runtime as owner until session and provider-client retirement actually succeed. A
  later `DisposeAsync` or `TeardownAsync` must resume remaining work instead of treating the failed attempt as
  terminal. Do not clear owned sessions or observer resources, and do not set a disposed/teardown-complete flag, until
  retirement is confirmed. Preserve failure collection and the order that drains completion observers only after the
  runtime has resolved session completion.

#### Review findings on 3bc12c16c4

Resolved by `3f0feed336`.

**Finding 1 — high**

- **Location:** `src/squad.CopilotSdk/CopilotSdkAgentRuntime.cs` (`DisposeAsync`, `myForceStop` / `myStopSucceeded` /
  `myClientDisposed`).
- **Violated behavior:** The runtime must remain owner until provider-client retirement succeeds. A later
  `DisposeAsync` must resume remaining stop/dispose work instead of leaving the client owned with no succeeding retry
  path.
- **Root cause:** Client disposal now runs only when `myStopSucceeded` is true. If session teardown already started
  `myForceStop` and that task faults, `DisposeAsync` awaits the same cached task, records the failure, and skips
  `myClient.DisposeAsync`. A retry awaits the same faulted task again, so stop never succeeds and the client is never
  disposed. The previous backend always attempted client disposal after stop failures.
- **Required outcome:** A failed shared-client stop, including a failed `myForceStop` task, must still leave retryable
  remaining work. Re-attempt stop and then dispose the client, or dispose the client even when stop failed, but do not
  leave the provider client owned after `DisposeAsync` has no remaining work that can succeed. Keep session-level
  retry, `SessionGeneration` failed-teardown reset, and observer drain only after confirmed runtime retirement.

### Slice 3: Make SessionRegistry the lifecycle authority

**Status: blocked until Slice 2 is accepted**

1. Extend `SessionRegistry` into the single lifecycle aggregate for `Created`, `Starting`, `Running`, `Stopping`, and
   `Stopped`. It owns generation identity, session leases, transition exclusion, and command admission; keep cohesive
   catalog and command-tracking mechanics in private/internal collaborators rather than one undifferentiated class.
2. Add `SquadLifecycleTransition` as an explicit handle returned by the registry. Every successful begin has exactly
   one commit or fail and always releases transition ownership. No I/O resource is owned by the transition or registry.
3. Replace unleased active-session lookup with generation-bound session and command leases. Route ViewModel commands
   and handoff notification through a narrow injected admission contract so phase checks and session selection are
   atomic. Do not create a reverse dependency from `squad.Application` to headquarters.
4. Make startup and shutdown use the registry transition. Beginning shutdown closes command admission and requests
   cancellation before generation teardown; admitted commands drain before the runtime owner retires sessions.
5. Preserve current public protocol and observable startup/shutdown behavior. Do not add `Relaunching`, `Failed`, or
   relaunch commands in this refactoring slice.

Slice 3 is accepted when:

- there is exactly one lifecycle phase, generation counter, transition exclusion mechanism, and command-admission
  owner;
- no production API returns an active session without a generation-bound lease;
- rejected commands are side-effect free, admitted commands drain before generation teardown, and handoff lookup uses
  the same current-generation authority;
- transition completion and release are failure-safe without lifecycle state in `SquadApplication` or
  `SquadViewModel`; and
- focused startup, command, handoff, failure, and shutdown scenarios remain green.

### Slice 4: Extract generation orchestration from the shell

**Status: blocked until Slice 3 is accepted**

1. Introduce `SquadRuntimeController` as the sole owner of the current `SessionGeneration`. It coordinates generation
   construction, registration, handoff recovery/start, rollback, replacement-ready teardown, and lifecycle transitions;
   it owns no lifecycle phase, command-admission state, window lifetime, host lease, workspace preparation, or sleep
   inhibition.
2. Define one internal generation-construction path and one rollback path. Startup and future relaunch call those same
   paths wherever their behavior is identical. A focused callback may bridge the window's sessions-started notification
   at the required boundary without transferring window ownership to the controller.
3. Reduce `SquadApplication` to process preparation, process-wide resource startup/disposal, the outer terminal-signal
   run loop, and delegation to the controller. Keep primary-versus-cleanup exception aggregation at the owner whose
   resources are being unwound; do not add a pass-through lifecycle facade.
4. Keep transition begin/commit/fail and command admission in `SessionRegistry`. The controller performs I/O under the
   transition contract but never duplicates phase or admission state.
5. Update composition in `Launch` and black-box support without widening public construction solely for tests. Add an
   architecture assertion that `SquadApplication` has no generation session, observer, backend, or handoff lifecycle
   fields.

Slice 4 and this refactoring are accepted when:

- `SquadApplication` owns only the outer run/termination loop and process-wide resources;
- `SquadRuntimeController` owns current-generation orchestration and `SessionGeneration` owns one generation's
  resources;
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