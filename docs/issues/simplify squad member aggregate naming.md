---
title: Name the member aggregate after the domain concept
priority: 1
---

## Goal

Name the live member aggregate `SquadMember` rather than exposing its architectural pattern in the type name.

## Scope

- Rename `SquadMemberAggregate` and its file to `SquadMember`.
- Rename `SquadMemberProcessor.Aggregate` to `Member` and update all dependent parameters, fields, and call sites.
- Update comments and documentation to use `SquadMember` as the code symbol while retaining “aggregate” where it
  describes the type's architectural role.

This is a naming-only refactor. Do not change member state, synchronization, lifecycle, routing, public contracts,
or behavior.

## Implementation plan

### Slice 1: Rename the live member state type

Rename `src/squad.Application/SquadMemberAggregate.cs` and its internal `SquadMemberAggregate` type and constructor
to `SquadMember`. Update every compile-time reference in `SquadMembers`, `SquadMemberProcessor`,
`SquadMemberEventProjector`, `OperationLease`, and `AbortLease`, plus the `MemberInteractionState` XML reference and
the related current-state description in `docs/issues/squad member naming inconsistent.md`. Keep lower-case
“aggregate” wording where it describes this type's architectural responsibility rather than its code symbol.

This slice deliberately leaves `SquadMemberProcessor.Aggregate` in place. That produces a complete, buildable
intermediate state: the domain object has its final code name while the processor's access surface still describes
the object's architectural role.

Acceptance criteria:

- The application source contains no `SquadMemberAggregate` type reference, and its source file is named
  `SquadMember.cs`.
- `SquadMemberProcessor.Aggregate` still exposes the renamed `SquadMember` without changing visibility, ownership,
  or mutation paths.
- No specification changes are needed; the full solution builds and the unchanged black-box acceptance suite
  passes.

### Slice 2: Name the processor-owned object as the member

Rename `SquadMemberProcessor.Aggregate` to `Member`, its constructor parameter from `aggregate` to `member`, and
every use inside the processor. Update all dependent access sites and locals in `SquadMembers`, including the
constructor local, snapshot and transcript composition, readiness lookup, and `GetMember`. Align nearby comments
and XML documentation so `SquadMember` denotes the code symbol and “aggregate” remains only as the architectural
description of its role.

Acceptance criteria:

- Runtime code contains neither a `SquadMemberProcessor.Aggregate` property nor a dependent `.Aggregate` access,
  and member-valued parameters, locals, and fields use member naming.
- The processor remains the sole mutation path, and member state, locking, mailbox ordering, routing, snapshots,
  transcripts, lifecycle, diagnostics, and protocol shapes are unchanged.
- No new tests or specification edits are introduced for this naming-only change; `dotnet build squad.slnx
  --nologo` and `dotnet test src\squad.Specs\squad.Specs.csproj --no-restore --nologo --verbosity minimal` pass.

## Acceptance criteria

- Runtime code contains no `SquadMemberAggregate` symbol or `SquadMemberProcessor.Aggregate` property.
- The full solution builds and the black-box acceptance suite passes unchanged.