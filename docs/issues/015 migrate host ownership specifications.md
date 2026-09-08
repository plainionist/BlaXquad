---
title: Migrate host ownership specifications
priority: 15
---

# Migrate host ownership specifications

This issue implements the [backend test strategy](../manual/test-strategy.md).

## Goal

Rewrite `HostOwnership.feature` around the behavior of the actual `squad-hq` processes and commands rather than
`HostLease`, `HostControlClient`, metadata structures, or raw named-pipe requests.

Retain operator-visible behavior such as exclusive launch, readiness waiting, timeout diagnostics, equivalent project
paths, shutdown, stale-host recovery, and idempotency. Replace internal lease and metadata assertions with observable
process outcomes. Delete malformed-pipe and exact-metadata scenarios when they do not represent a supported public
contract.

## Acceptance criteria

- Every retained scenario runs one or more actual `squad-hq` processes.
- Duplicate launch is observed through command failure while the first host remains usable.
- Readiness and busy/unknown-role outcomes are observed through `squad-hq wait-for-agent`.
- Shutdown is requested through `squad-hq shutdown`, including path-equivalence and idempotency cases.
- Stale ownership is proven by successfully starting or controlling a replacement host, not by inspecting cleanup
  leases.
- Step definitions do not construct `HostLease`, `HostControlClient`, or `NamedPipeClientStream`.
- Tests do not read `PipeName` or assert the exact shape of `.blaxquad/host.json`.
- Low-level scenarios without a user-visible or compatibility contract are deleted rather than preserved through new
  test hooks.
- Host ownership behavior and diagnostics remain covered on every supported platform.

## Implementation plan

### Slice 1: Migrate ownership and shutdown lifecycle

**Status: complete (63da8c3ab7)**

1. Extend the test-owned process facade only with the semantic operations needed to start, address, stop, and
   deliberately terminate an exact `squad-hq` child process. Keep executable paths, process handles, provider
   descriptors, captured output, bounded waits, and emergency cleanup inside support code.
2. Rewrite the duplicate-launch scenario to start a real provider-free headquarters process, invoke a second real
   `squad-hq launch`, assert its operator-facing failure without an exception trace, and prove the original host is
   still usable through a successful public command before shutting it down normally.
3. Rewrite normal, equivalent-path, and repeated shutdown scenarios to use `squad-hq shutdown` against actual host
   processes and assert command results plus process completion rather than lease or metadata state.
4. Replace fabricated stale metadata with an abruptly terminated headquarters process, then prove stale ownership
   recovery by starting and controlling a replacement process for the same project.
5. Delete the metadata-shape, direct lease-acquisition, and malformed/raw control-pipe scenarios because they expose
   unsupported implementation details rather than operator behavior.

Slice acceptance:

- Every retained ownership/shutdown scenario launches the published `squad-hq` executable or invokes one of its real
  commands.
- A duplicate launch fails clearly while the original host still answers a public command and shuts down cleanly.
- Normal and equivalent-path shutdown terminate the addressed host, and shutdown remains successful when no host is
  running.
- A replacement host starts and remains controllable after the prior host is terminated without normal shutdown.
- The migrated steps do not inspect `.blaxquad/host.json`, acquire `HostLease`/cleanup leases, or send raw named-pipe
  requests.

### Slice 2: Migrate readiness and timeout diagnostics

**Status: changes requested (b25f8d21b3)**

1. Drive readiness through a real `squad-hq` host using the controllable fake provider. Start `wait-for-agent` as a
   separate CLI process while the configured role is busy, prove the command remains blocked, emit the provider event
   that makes the role ready, and assert the command succeeds.
2. Cover busy timeout and unknown-role diagnostics through `squad-hq wait-for-agent`, using bounded process waits and
   captured command output rather than an injected readiness delegate.
3. Retain main-checkout, linked-worktree, and equivalent-explicit-path discovery cases by addressing the same live host
   from each user-visible path form.
4. Retain argument-validation and missing-project behavior as real command invocations. Cover an unavailable host
   endpoint by terminating a launched host without shutdown and asserting the public command respects its requested
   deadline and reports actionable diagnostics.
5. Consolidate the feature's process setup, command execution, output capture, and teardown behind semantic
   test-support APIs, removing the remaining direct references to host-control product types from the step
   definitions.

Slice acceptance:

- Readiness blocking, success, busy timeout, and unknown-role outcomes are all observed through real
  `squad-hq wait-for-agent` processes.
- Main-checkout, linked-worktree, and equivalent explicit paths all reach the same live host.
- Invalid timeout, project discovery, and unavailable-host failures preserve their operator-facing diagnostics and
  bounded completion behavior.
- `HostOwnershipSteps` constructs no `HostLease`, `HostControlClient`, `CleanupLease`, or `NamedPipeClientStream`, does
  not access `PipeName`, and does not inspect the host metadata file.
- The complete `HostOwnership.feature` passes through the existing black-box acceptance suite on every supported
  platform without arbitrary synchronization sleeps.

#### Review findings on b25f8d21b3

**Finding 1 — high**

- **Location:** `src/squad.Specs/StepDefinitions/HostOwnershipSteps.cs`
  (`ThenTheExecutableRemainsWaitingForAgentReadiness`),
  `src/squad.Specs/Features/HostOwnership.feature` (Waiting for an agent blocks until the live host reports it ready).
- **Violated behavior:** Slice 2 must start `wait-for-agent` as a separate CLI process while the role is busy, prove
  the command remains blocked, then emit the provider event that makes the role ready. The complete feature must pass
  without arbitrary synchronization sleeps.
- **Root cause:** After `StartWaitForAgent`, the Then sleeps 200 ms and asserts `IsRunning`. That does not wait for an
  observable that the command has reached the live host, and it is the same class of fixed sleep the slice forbids.
  A command that never contacts the host, or that succeeds in under 200 ms, is not distinguished from a command blocked
  on busy readiness.
- **Required outcome:** Prove `wait-for-agent` remains blocked using a bounded wait on an observable (the command is
  still running after it has contacted the live busy host). Then emit idle and assert success. Do not use
  `Thread.Sleep`.
