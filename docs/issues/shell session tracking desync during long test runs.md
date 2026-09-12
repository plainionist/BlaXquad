---
title: shell session tracking desync during long test runs
priority: 30
---

## Symptom

While validating a change, the coder role started `dotnet test` for the full `squad.Specs` suite in a tracked
background shell (a multi-minute run). After the initial wait elapsed the shell moved to the background as
expected. A subsequent read of that shell returned "The execution of this tool, or a previous tool was
interrupted", and a listing of active shells showed none at all - the tracked handle was gone. The coder then
started a fresh `dotnet test` run, which immediately failed with an MSB4018 file-lock error on
`squad.Process.deps.json`, because a build/test process was still writing that file. A third attempt, right
after, succeeded. Net effect: the coder appeared to "lose track" of a long-running test run, and the very next
build stumbled over a file left locked by a process the coder could no longer see or account for.

## Root cause

The shell-tracking layer (the `shellId` bookkeeping used by `powershell` / `read_powershell` / `list_powershell`)
and the lifecycle of the OS process it launched are only loosely coupled:

1. A `sync` command that outlives `initial_wait` is hard to distinguish, from the caller's side, between "still
   running in the background, handle intact" and "harness lost the handle" - both eventually surface as an
   unreadable or absent session. The tool does not currently expose a definitive, independent signal such as
   "process exited with code X at time T" once its own session bookkeeping has been disrupted.
2. Losing the *tracking handle* is not the same event as the *process* exiting. In this incident the handle was
   dropped (`list_powershell` showed no active sessions) while the previously started `dotnet test` invocation
   was, in fact, still alive and holding a write lock on its own build output - proven by the immediate next
   build attempt failing on that exact file, and a retry moments later succeeding once the orphan had finished.
3. Per the coder role's build/test safety rules, losing the execution handle and root PID must not be followed
   by a machine-wide process scan or name-based cleanup. That rule is correct and should stay, but it leaves no
   sanctioned way to confirm whether an orphaned process is still running, still holding locks, or already gone.
   The only recourse is to retry and let the ambiguity resolve itself, which is what happened here.
4. Because "handle lost" and "process still running" look identical from the caller's side, the safest-looking
   response is to assume the run may still be in flight and either wait longer or retry - which is indistinguishable,
   from the outside, from "believing tests are still running" even after they have in fact finished. The perceived
   pattern of "thinking tests are still running and continuing to wait" is this ambiguity being resolved in the
   conservative direction every time, not a reasoning error about any single run's actual state.

## Why this is not a simple retry-loop bug

The coder did not spin retrying the same run - it correctly stopped waiting once `list_powershell` reported no
active sessions, reported the lost handle instead of scanning for orphaned processes, and started one fresh,
independently tracked run. The failure mode is upstream of that: the tooling gives no reliable way to tell
"the previous run already finished, its output just wasn't delivered" apart from "the previous run is still
executing and will finish shortly", so every loss of tracking is followed by at least one avoidable build/test
attempt that can collide with the still-finishing orphan.

## Suggested improvements (outside this squad's remit to implement directly)

- Have the shell-tracking layer persist a terminal status (exit code, completion timestamp) for a session even
  after its live handle is disrupted, so `read_powershell`/`list_powershell` can report "already completed with
  exit code N" instead of only "no active sessions".
- Where possible, avoid tearing down the tracking handle for a still-running process; if the handle must be
  dropped, surface that distinction explicitly ("handle dropped, process state unknown" vs. "process confirmed
  exited") so the caller does not have to infer it from a subsequent, unrelated file-lock failure.

## Scope

The shell-session registry and the process lifecycle behind `powershell` are supplied by the external agent
harness; no BlaXquad module owns or can repair that state. The repository-level change is therefore limited to
making the coder's operating policy safe under an unknown process state. It must not add product code, a
repository-specific process supervisor, machine-wide process discovery, lock-file probing, or a retry delay that
merely guesses when an orphan has exited. Durable terminal status remains an upstream tooling improvement.

## Implementation plan

### Slice 1: Stop validation after build/test tracking is lost

**Task:** `stop-after-lost-build-test-tracking`

**Logical change:** Make loss of both the execution handle and root PID a hard validation blocker, so an
unaccounted-for build or test can never be overlapped by a replacement invocation in the same worktree.

**Implementation:**

- Update `blaxquad/roles/coder.prompt` only. Keep direct `dotnet build` and `dotnet test` execution, the one-command
  limit, and the prohibition on machine-wide process inspection or name-based cleanup.
- Require builds and tests to use synchronous tool execution with an initial wait chosen to cover the expected
  duration; use the maximum supported initial wait for the full `squad.Specs` suite. If the command continues in
  the background, retain and read the same shell handle rather than starting another invocation.
- Distinguish a known root PID from fully lost tracking. A retained PID may still be monitored or terminated
  directly with its known descendant tree, but it must never be rediscovered through process enumeration.
- If both handle and root PID are lost, classify the invocation as still potentially running. Do not launch another
  build or test, use another command as a lock probe, infer completion from elapsed time, or claim that validation
  passed.
- Require the coder to stop validation and send the architect a `squad handoff note` identifying the untracked
  command and the fact that its result is unknown. Work may resume only after the operator supplies a fresh,
  confirmed-safe execution context; the coder must not manufacture that confirmation.

**Acceptance:**

- The coder prompt makes the long-running synchronous invocation and same-handle continuation the default.
- At most one build or test can be in flight or in an unknown state for the current worktree.
- Losing both tracking identifiers cannot lead to a replacement build/test invocation or a reviewer handoff that
  claims successful validation.
- A still-known PID remains usable only for targeted lifecycle operations; machine-wide and process-name discovery
  remain forbidden.
- The blocked state is reported explicitly to the architect instead of being hidden by retries or treated as a
  product test failure.
