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

Build on the existing `SessionGeneration` and `SquadLifecycleTransition` contracts rather than adding another parallel
lifecycle state machine. Startup, relaunch, failure rollback, and shutdown must continue to use `SessionRegistry` as the
authoritative phase and command-admission owner.

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