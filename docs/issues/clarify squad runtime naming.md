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

## Architecture

The application model and its runtime owner remain separate objects with the existing dependency direction:
`squad.Runtime` owns and coordinates one `squad.Application` model. Both continue to represent the same
`SquadGenerationId` and retire together. This work changes only their names and the identifiers and documentation
that describe them; it does not add a generation layer, move responsibilities, or alter any lifecycle or protocol.

Rename the runtime coordinator first. That leaves an accurate, independently useful intermediate state and avoids
temporarily using `Squad` for both the application model and runtime coordinator, even though they are in different
namespaces.

## Implementation plan

### Slice 1 - Name the runtime coordinator `SquadRuntime` [done]

**Status:** complete (fe435edcc6)

**Outcome:** `squad.Runtime.SquadRuntime` unambiguously names the owner and lifecycle coordinator for one squad
generation.

- Rename `src/squad.Runtime/Squad.cs` and its type and constructor to `SquadRuntime`.
- Update the runtime coordinator references in `Headquarters`, `SessionGeneration`, and the fake-provider commentary.
  Rename fields and locals that hold this coordinator to `squadRuntime`; retain operation names such as
  `ReplaceSquadAsync` and existing user-facing text because they describe squad operations rather than the old type.
- Update XML documentation and stable manual references that currently use `Squad` for the runtime coordinator,
  including the lifecycle-owner wording in `docs/manual/glossary.md` and the internal test-boundary examples in
  `docs/manual/test-strategy.md`. At this intermediate boundary, keep `SquadMembers` as the application-model name.
- Preserve construction, startup, failure signaling, retirement order, ownership, visibility, and all public
  contracts exactly.

**Acceptance criteria:**

- The runtime lifecycle type and file are named `SquadRuntime`; no code or type-specific documentation still calls
  that coordinator `Squad`.
- `Headquarters` still owns at most one coordinator and performs the same installation, startup, replacement, and
  retirement sequence.
- `dotnet build squad.slnx` succeeds and the unchanged black-box `squad.Specs` suite passes.

**Status: complete (fe435edcc6).** `squad.Runtime.Squad` and its file are now `SquadRuntime`. Headquarters field and
local `mySquad`/`squad` are `mySquadRuntime`/`squadRuntime`; operation names such as `ReplaceSquadAsync` are
retained. `SessionGeneration`, FakeAgentRuntime commentary, glossary lifecycle-owner wording, and test-strategy
internal examples now name the coordinator `SquadRuntime`. `SquadMembers` remains the application-model name.
Naming-only; no specification changes.

### Slice 2 - Name the application model `Squad`

**Status:** Pending

**Outcome:** `squad.Application.Squad` unambiguously names the authoritative team model that owns the member
directory and member operations.

- Rename `src/squad.Application/SquadMembers.cs` and its type and constructor to `Squad`.
- Update all dependent types and call sites in `squad.Application` and `squad.Runtime`. Rename fields, properties,
  parameters, and locals that hold the whole model from `members` to `squad`; retain `members` only for actual member
  collections or definitions. In particular, make `SquadRuntime` own/expose its application `Squad`, and keep
  `SquadViewModel` as the process-lifetime facade over the installed `Squad`.
- Update XML documentation and stable manual references, especially the session-generation glossary and
  test-boundary examples, so `Squad` consistently means the application model and `SquadRuntime` consistently means
  its runtime owner.
- Keep `SquadGenerationId`, the one-generation lifecycle, object ownership, command routing, synchronization,
  serialization, and all public behavior unchanged. Do not introduce `SquadGeneration`.

**Acceptance criteria:**

- The application model type and file are named `Squad`; no code or type-specific documentation still refers to
  `SquadMembers`.
- Names at every dependency make the boundary explicit: `SquadRuntime` coordinates resources and owns one `Squad`,
  while `Squad` owns member state and operations.
- No `SquadGeneration` type is introduced and `SquadGenerationId` is unchanged.
- `dotnet build squad.slnx` succeeds and the unchanged black-box `squad.Specs` suite passes.

After both slices are accepted, delete this issue file as the completion-only cleanup.