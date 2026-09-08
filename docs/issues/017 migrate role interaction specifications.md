---
title: Migrate role interaction specifications
priority: 17
---

# Migrate role interaction specifications

This issue implements the [backend test strategy](../manual/test-strategy.md).

## Goal

Rewrite the prompt, abort, readiness, role-isolation, and pending-interaction scenarios from `ViewModel.feature` as
user-driven exchanges between the headless UI and fake provider.

Retain behavior that a user or provider can observe. Remove scenarios that only prove a private collection, event
counter, semaphore, or method call.

## Acceptance criteria

- Prompts sent as real UI protocol commands reach only the selected fake role session.
- Manual prompts remain serialized per role without blocking independent roles.
- Abort reaches the selected role, cancels the active operation, and leaves the role in the documented subsequent
  state.
- Readiness reflects idle/busy work and remains correct while multiple role sessions start.
- Permission, input, and elicitation requests appear through published UI state and responses return to the owning
  fake session.
- Wrong-role, duplicate, late, aborted, and shutdown interaction responses retain their supported outcomes.
- Identical request IDs for different roles remain isolated where that behavior is supported.
- Provider/session failure produces the documented role availability, error, and command-rejection behavior.
- Steps interact only with the scenario driver's UI and agent APIs.
- Steps do not access `SquadViewModel`, role dictionaries, pending-interaction collections, operation coordinators,
  sessions, or `Recording*` objects.
- Duplicate or implementation-only scenarios are deleted only after their supported behavior is covered through the
  process boundary.
