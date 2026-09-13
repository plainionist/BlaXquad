---
title: Name the squad model and its runtime accurately
priority: 2
---

## Goal

Use `Squad` for the authoritative application model of the team, and `SquadRuntime` for the runtime resource and
lifecycle coordinator that operates it.

## Scope

- Rename `squad.Application.SquadMembers` and its file to `Squad`.
- Rename `squad.Runtime.Squad` and its file to `SquadRuntime`.
- Update dependent fields, properties, parameters, locals, call sites, comments, and stable documentation.
- Keep `SquadGenerationId` unchanged. Do not introduce a `SquadGeneration` type: `Squad` and `SquadRuntime` have the
  same one-generation lifecycle.

This is a naming-only refactor. Do not change ownership, lifecycle, routing, synchronization, public contracts, or
behavior.

## Acceptance criteria

- `Squad` names the team model that owns its members and member operations.
- `SquadRuntime` names the coordinator that owns the backend, sessions, handoff pump, startup, and retirement.
- The full solution builds and the black-box acceptance suite passes unchanged.