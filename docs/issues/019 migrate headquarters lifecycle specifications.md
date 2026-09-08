---
title: Migrate headquarters lifecycle specifications
priority: 19
---

# Migrate headquarters lifecycle specifications

This issue implements the [backend test strategy](../manual/test-strategy.md).

## Goal

Rewrite startup, shutdown, cancellation, provider failure, resource cleanup, generation isolation, and command-draining
scenarios from `ViewModel.feature` around observable behavior of the real `squad-hq` process.

Use the fake provider and headless UI to control meaningful asynchronous boundaries. Replace exact internal lifecycle
traces with outcomes that prove safety: process termination, error reporting, command rejection, absence of stale
updates, released host ownership, and successful subsequent launch.

This issue must not add relaunch behavior from `restart button.md`.

## Acceptance criteria

- Healthy startup reaches UI and agent readiness and shuts down cleanly.
- Shutdown requested before or during startup prevents the host from becoming available and releases acquired
  resources.
- Window/input closure, caller cancellation, host-control shutdown, provider failure, and handoff failure produce the
  documented terminal result and diagnostics.
- Partial provider startup is retired and does not leave a live session or owned host behind.
- Accepted commands reach a safe terminal outcome before provider resources disappear; newly rejected commands have no
  side effects.
- Delayed events from a terminated session cannot change subsequently published state.
- Primary and cleanup failures remain observable without requiring injected exception collectors.
- Resource release is proven by process exit, closed control endpoints, durable state, and the ability to start a new
  host.
- Scenarios do not construct `SquadApplication`, inject startup delegates, inspect session registries, or assert
  `LifecycleTrace` entries.
- Exact interleavings or callback order without a user-visible or resource-safety consequence are deleted.
- Existing supported lifecycle behavior remains unchanged.
