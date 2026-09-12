---
title: Extract the shared squad domain assembly
priority: 40
---

# Extract the shared squad domain assembly

## Problem

The stable concepts that identify a squad and its participants currently live in the modules that first needed
them. In the current model, `RoleRow` is defined in `squad.Configuration` even though handoff delivery, workspace
preparation, and the role-facing CLI all consume it. `MemberConfigRow` independently carries an overlapping, larger
description during workspace preparation, while `MemberConfiguration` carries a smaller application projection.
Other closed squad-wide concepts, such as receive mode and member status, are represented as strings at several
boundaries.

This makes infrastructure assemblies appear to own product vocabulary and permits parallel representations of the
same participant. It also leaves no dependency-free home for immutable squad concepts shared by configuration,
workspace preparation, runtime, application state, handoff routing, and presentation.

Issue 024 established the underlying squad-member-centered model. This extraction uses that implemented model as its
baseline and must not reopen its semantic decisions.

## Goal

Introduce a dependency-free `squad.Domain` assembly containing only the stable, immutable vocabulary shared across
the product. Move the resulting squad and member descriptors into that assembly, replace the current `RoleRow`,
`MemberConfigRow`, and `MemberConfiguration` duplication with one canonical type, and replace closed string values
with domain enums where the values are already authoritative and exhaustive.

The assembly is a small shared kernel. It is not a general home for every important or widely referenced type.

## Relationship to issue 024

Issue 024 owns the semantic redesign: Headquarters and squad lifetimes, reusable roles, unique squad members,
member-local state ownership, addressing, configuration shape, and naming across the product. That work is present
in the current codebase and is the baseline for this extraction.

This issue must use the vocabulary and concrete types produced by issue 024. Names below describe responsibilities,
not permission to introduce competing versions of its model. In particular, this issue must not redesign the member
aggregate, configuration schema, runtime lifecycle, or role-to-member cardinality.

## Current codebase analysis

The issue-024 model is now present: schema-version-2 configuration separates reusable role names from uniquely named
squad members, multiple members may reference one role, and the configured leader identifies a member. The shared
immutable description produced from that model is still fragmented across several assemblies:

- `SquadMemberConfiguration` in `squad.Configuration` is the validated persisted-input representation;
- `MemberConfigRow` in `squad.Workspaces` adds the resolved worktree path and flattens normalized agent settings;
- `MemberConfiguration` in `squad.Application` carries only member identity, display name, and role; and
- `RoleRow` in `squad.Configuration` is a legacy-named member projection used by the role-facing CLI and handoff
  delivery.

`PreparedLaunch` consequently carries members, leader identity, and a second handoff-member projection separately,
while `SquadMembers` reconstructs its own ordered member list and leader field. Receive mode remains a validated
`task`/`batch` string, and member lifecycle status remains a mutable string projected as `starting`, `running`,
`idle`, `stopped`, or `error`.

## Required design

### Dependency-free shared kernel

Create `squad.Domain` with no project references. A type belongs there only when it:

- names a stable squad-wide concept rather than an adapter or workflow phase;
- is meaningful across multiple product boundaries without reference to those boundaries;
- can remain independent of JSON, filesystems, Git, processes, providers, hosting, UI, and protocol transport; and
- owns immutable data or a closed domain vocabulary, not mutable application state or resource lifecycle.

Cross-assembly usage alone is not sufficient reason to move a type.

### Initial domain surface

Extract the post-024 equivalents of these concepts:

- distinct role and squad-member identities;
- the immutable description of one configured and resolved squad member, including its identity, display name, role
  reference, resolved worktree identity, receive mode, and normalized agent settings;
- the immutable ordered squad roster or definition, including its leader member identity;
- receive mode as the closed `task`/`batch` vocabulary; and
- squad-member lifecycle status as the closed values currently published as `starting`, `running`, `idle`, `stopped`,
  and `error`.

Use the names established by issue 024. Replace the current `RoleRow`, `MemberConfigRow`, and `MemberConfiguration`
with the one canonical member descriptor rather than moving a `Row` type unchanged. Do not retain aliases, wrappers,
or duplicate compatibility records for internal callers.

The concrete initial `squad.Domain` surface is:

- `RoleId`: the identity of one reusable role definition;
- `SquadMemberId`: the distinct operational identity of one configured squad member;
- `ReceiveMode`: the closed `Task`/`Batch` vocabulary;
- `SquadMemberStatus`: the closed `Starting`/`Running`/`Idle`/`Stopped`/`Error` lifecycle vocabulary;
- `AgentSettings`: normalized permissions, model, and effort values, without configuration serialization concerns;
- `SquadMemberDefinition`: one immutable resolved member description containing `SquadMemberId`, display name,
  `RoleId`, worktree name and resolved path, `ReceiveMode`, and `AgentSettings`; and
- `SquadDefinition`: the immutable ordered list of `SquadMemberDefinition` values and the leader's
  `SquadMemberId`.

`SquadMemberDefinition` replaces `MemberConfigRow`, `MemberConfiguration`, and `RoleRow`; none of those records
survive as a wrapper or reduced internal projection. `SquadDefinition` becomes the single value carried between
workspace preparation, application construction, provider-context projection, and handoff delivery instead of
passing member order, leader identity, and handoff-member rows independently.

The identity types distinguish values that are currently all represented as strings; they do not parse
configuration or perform validation tied to JSON, files, prompts, or worktrees. Worktree name and resolved path stay
data on `SquadMemberDefinition`; no additional worktree value-object hierarchy is required. External strings are
mapped explicitly at their owning boundaries rather than by putting JSON attributes or protocol concerns in the
domain types.

The active runtime `Squad` and mutable member aggregate are not immutable descriptors. They continue to live with
the application or runtime code that owns provider sessions, command admission, event processing, synchronization,
and generation resources.

### Keep configuration at the boundary

`SquadConfiguration`, `SquadMemberConfiguration`, `SquadAgentConfiguration`, their JSON document types,
configuration exceptions, and loaders remain in `squad.Configuration`. They model and validate persisted input.
Configuration and workspace preparation map validated input and resolved paths into domain values; the domain
assembly does not read configuration files or resolve filesystem paths.

Serialization names such as `task`, `batch`, and member-status strings remain stable at external boundaries. Parsing
and serialization belong to the owning adapters unless a dependency-free conversion is genuinely part of the
domain value itself.

### Preserve bounded module ownership

Do not move these cohesive module contracts into `squad.Domain`:

- handoff documents, kinds, payloads, queue state, validation, and persistence from `squad.Handoffs`;
- agent sessions, provider events, interactions, responses, and backend contexts from
  `squad.AgentProvider.Abstractions`;
- mutable member state, command coordination, event projection, and runtime lifecycle owners;
- transcript state, retention, archive behavior, and UI-facing transcript records;
- issue-catalog records, UI protocol messages, hosting contracts, process results, workspace orchestration, or Git
  operations.

Those concepts may be central features, but they already have narrower owners and independent reasons to change.
Moving them would turn `squad.Domain` into a miscellaneous shared-types assembly.

### Dependency direction

Configuration, workspace, application, runtime, handoff-delivery, provider-boundary, and presentation modules may
reference `squad.Domain` when they consume the extracted values. `squad.Domain` must not reference any of them.

Avoid pass-through abstractions whose only purpose is hiding the new project reference. Boundary-specific DTOs may
project from domain values when their shape is genuinely provider-, persistence-, or protocol-specific.

## Implementation plan

1. Treat the implemented issue-024 squad, role, member, addressing, and configuration semantics as the baseline.
2. Add `squad.Domain` to the solution with no project references.
3. Add `RoleId`, `SquadMemberId`, `AgentSettings`, `SquadMemberDefinition`, and `SquadDefinition` without changing
  the established post-024 semantics.
4. Replace `MemberConfigRow`, `MemberConfiguration`, and `RoleRow` with the canonical domain values and update
  consumers to use the ordered `SquadDefinition`.
5. Add `ReceiveMode` and `SquadMemberStatus`, replacing authoritative internal strings while preserving existing
  configuration and protocol representations through boundary mappings.
6. Update project references, namespaces, and `docs/Manual/modules.md` to describe the shared kernel and its strict
   boundary.
7. Build and publish from clean outputs, then run the existing black-box Gherkin suite through the real CLI and UI
   protocol boundaries.

## Acceptance criteria

- The implemented issue-024 semantics remain unchanged.
- `squad.Domain` exists in the solution and has no project references.
- Its initial product-facing surface is limited to `RoleId`, `SquadMemberId`, `ReceiveMode`, `SquadMemberStatus`,
  `AgentSettings`, `SquadMemberDefinition`, and `SquadDefinition`.
- The configured, resolved squad member has one canonical representation; no surviving `RoleRow`, `MemberConfigRow`,
  `MemberConfiguration`, or equivalent parallel row records remain.
- Role identity and squad-member identity remain distinct according to the model established by issue 024.
- Ordered squad membership and leader selection are represented without duplicating a separate leader/member model in
  each consumer.
- Receive mode and squad-member lifecycle status are typed internally, while external JSON and protocol values remain
  compatible.
- Configuration records, documents, parsing, validation, and filesystem path resolution remain outside
  `squad.Domain`.
- Handoff, provider, interaction, transcript, issue, UI, hosting, process, and workspace implementation types retain
  their cohesive module owners.
- No compatibility assembly, type forwarder, deprecated alias, or pass-through wrapper preserves the displaced
  internal types.
- Stable module documentation describes `squad.Domain` and the modules that map to or consume it.
- The product builds and publishes successfully, and the existing black-box Gherkin suite passes without observable
  behavior, persistence, ordering, or protocol regressions.

## Non-goals

- Implementing or revising the squad-member-centered model owned by issue 024.
- Changing the `blaxquad/squad.json` schema, role prompts, leader semantics, member addressing, or restart behavior.
- Moving mutable active-squad or member aggregates into the dependency-free assembly.
- Moving every broadly used record or enum into one project.
- Moving handoff documents, provider events, interaction contracts, transcript projections, UI messages, or
  infrastructure contexts.
- Introducing value-object wrappers for every string identifier without an established invariant or ambiguity to
  resolve.
- Changing public protocol payloads, persisted handoff formats, queue layout, or user-visible behavior.
- Adding tests whose only purpose is proving that an old assembly or type name is unavailable.