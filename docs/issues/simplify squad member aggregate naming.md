---
title: Name the member aggregate after the domain concept
priority: 2
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

## Acceptance criteria

- Runtime code contains no `SquadMemberAggregate` symbol or `SquadMemberProcessor.Aggregate` property.
- The full solution builds and the black-box acceptance suite passes unchanged.