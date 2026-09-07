---
title: Contract the backend surface exposed for tests
priority: 22
---

# Contract the backend surface exposed for tests

This issue implements the [backend test strategy](../manual/test-strategy.md).

## Dependency

- `021 remove legacy white box test harness.md`

## Goal

Remove production API, indirection, nullability, callbacks, overloads, and state exposure that no longer has a
production caller after the specification migration.

This is the final behavior-preserving cleanup. Determine the final surface from production call sites rather than
preserving members because old tests once used them.

Likely candidates include:

- direct `SquadApplication` construction and inspection properties;
- public `SquadViewModel` state collections and test event/request injection;
- test-only lifecycle and event sinks;
- optional `viewModel`, `hostLease`, preparation, and message callbacks;
- public protocol publishers, journals, serializers, and recovery helpers;
- host lease, window host, sleep inhibitor, and event-channel observation properties;
- test-only handoff poller overloads and stop delegates; and
- provider session capacities, timeouts, and callbacks not varied by production.

Private delegates that are the simplest implementation of one cohesive responsibility and nullable values that model
real domain absence are not targets.

## Acceptance criteria

- Every remaining public member has a production caller or is an intentional provider/UI SPI documented in
  `docs/manual/test-strategy.md`.
- No product constructor or method accepts a delegate, nullable collaborator, timing knob, or alternate implementation
  solely for tests.
- `SquadApplication.ViewModel`, `SquadApplication.Sessions`, direct mutable role/pending-interaction inspection, and
  comparable white-box probes are removed.
- Protocol and lifecycle helpers used only inside their owning assemblies are internal.
- No `InternalsVisibleTo`, reflection-based access, `ForTests` API, test environment branch, or replacement test facade
  is introduced.
- Provider selection and the headless UI remain cohesive production SPIs; the fake provider and its control protocol
  remain entirely in `squad.Specs`.
- The change is a net reduction in public members, overloads, optional branches, and injected callbacks.
- All backend specifications continue to pass through the process boundary with no supported behavior change.
- No relaunch or frontend behavior from `restart button.md` is introduced.
