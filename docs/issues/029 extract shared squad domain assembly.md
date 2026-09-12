---
title: Extract the shared squad domain assembly
priority: 40
---

# Extract the shared squad domain assembly

## Problem

The `squad.Domain` assembly now provides a dependency-free shared foundation, but the stable concepts that identify a
squad and its participants still live in the modules that first needed them. In the current model, `RoleRow` is
defined in `squad.Configuration` even though handoff delivery, workspace preparation, and the role-facing CLI all
consume it. `MemberConfigRow` independently carries an overlapping, larger description during workspace preparation,
while `MemberConfiguration` carries a smaller application projection. Other closed squad-wide concepts, such as
receive mode and member status, are represented as strings at several boundaries.

This makes infrastructure assemblies appear to own product vocabulary and permits parallel representations of the
same participant. The dependency-free home now exists, but the immutable squad concepts shared by configuration,
workspace preparation, runtime, application state, handoff routing, and presentation have not yet moved into it.

Issue 024 established the underlying squad-member-centered model. This extraction uses that implemented model as its
baseline and must not reopen its semantic decisions.

## Goal

Use the dependency-free `squad.Domain` assembly for the stable, immutable vocabulary shared across the product. Move
the resulting squad and member descriptors into that assembly, replace the current `RoleRow`, `MemberConfigRow`, and
`MemberConfiguration` duplication with one canonical type, and replace closed string values with domain enums where
the values are already authoritative and exhaustive.

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

## Current implementation status

The shared foundation is complete:

- `squad.Domain` exists in `squad.slnx`, targets the repository's standard .NET framework settings, and has no
  project references;
- each of the other 19 C# projects has one direct reference to `squad.Domain`;
- `Contracts.cs` is currently its only C# source file and defines the shared `System.Contract` guard utility; and
- `dotnet build squad.slnx --nologo` succeeds for all 20 projects.

The domain extraction itself remains outstanding: none of the squad identity, definition, receive-mode, or status
types below has been added yet, and the parallel member records still exist.

The stable manual is also only partly aligned with the issue-024 baseline. `docs/Manual/modules.md` does not describe
`squad.Domain`, while `docs/Manual/architecture.md` and `docs/Manual/glossary.md` still say that worktrees, provider
sessions, receive modes, agent settings, and the leader belong to a role. The implemented schema assigns those
concerns to uniquely named squad members, which may share a reusable role. This issue must correct those directly
related descriptions without renaming the intentionally role-shaped CLI, handoff, provider, or UI protocol fields.

## Required design

### Dependency-free shared kernel

Keep `squad.Domain` free of project references. Except for the foundational `System.Contract` guard utility already
present, a type belongs there only when it:

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

The concrete initial product-domain surface is:

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

Every other C# project references `squad.Domain`, allowing configuration, workspace, application, runtime,
handoff-delivery, provider-boundary, and presentation modules to consume the extracted values directly.
`squad.Domain` must not reference any of them.

Avoid pass-through abstractions whose only purpose is hiding the new project reference. Boundary-specific DTOs may
project from domain values when their shape is genuinely provider-, persistence-, or protocol-specific.

## Implementation slices

### Slice 1 - Carry one canonical squad definition through Headquarters [done]

**Outcome:** Workspace preparation, provider-context projection, application construction, and handoff delivery all
consume one immutable ordered squad definition instead of independently projecting the same configured members.

- Add `RoleId`, `SquadMemberId`, `AgentSettings`, `SquadMemberDefinition`, and `SquadDefinition` to `squad.Domain`,
   with one top-level type per file. Keep them independent of configuration, JSON, filesystems, providers,
   application state, and protocol serialization. `SquadDefinition` must defensively retain configured member order
   and the leader's member identity; the two identity types must remain distinct throughout internal code. Keep the
   already validated receive-mode value as a string on `SquadMemberDefinition` in this slice only; slice 3 replaces
   that property with the closed domain enum without changing roster ownership again.
- Keep `SquadConfiguration`, `SquadMemberConfiguration`, and `SquadAgentConfiguration` as persisted-input boundary
   models. After validation and worktree-path resolution, map them once into the domain definition. Let the mutable
   workspace preparation context retain that definition, and make `PreparedLaunch` carry the single
   `SquadDefinition` rather than separate members, leader, and handoff-member projections.
- Project `AgentBackendContext` from the domain definition at the provider boundary. Construct `SquadMembers` from
   the same definition, retain its typed member order and leader, and use `SquadMemberId` and `RoleId` for
   authoritative application identity, including member aggregates and internal operation messages. Convert to the
   existing string addresses only where provider, transcript, command, or UI contracts still require them.
- Give handoff delivery the same `SquadDefinition`; index delivery paths and notifications by member identity while
   preserving the existing handoff document and notifier strings. Remove the now-unused
   `squad.Handoffs -> squad.Configuration` project reference and `squad.Runtime`'s
   `squad.Configuration` namespace dependency.
- Delete `MemberConfigRow` and `MemberConfiguration` rather than retaining aliases, wrappers, or reduced
   projections. Leave `RoleRow` and the role-facing `squad` command path unchanged until slice 2, where that separate
   process boundary can be migrated and accepted independently.
- Add the `squad.Domain` shared-kernel boundary to `docs/Manual/modules.md`. Align the directly affected squad,
   member, role, leader, worktree, provider-session, and configuration descriptions in
   `docs/Manual/architecture.md` and `docs/Manual/glossary.md` with the implemented issue-024 model, while retaining
   the public role-shaped vocabulary of existing commands and protocols.

**Acceptance:** `squad.Domain` still has no project references. Headquarters launch carries one ordered
`SquadDefinition` from resolved configuration into backend setup, `SquadMembers`, and handoff delivery;
`PreparedLaunch` has no parallel member, leader, or handoff-member fields. `MemberConfigRow` and
`MemberConfiguration` no longer exist, and no equivalent replacement projections are introduced. Two members may
still share one role while retaining distinct sessions and worktrees; configured order and leader selection remain
unchanged in `state.snapshot`; handoff fan-out and wake-up still use member addresses; all current provider and UI
payload strings remain unchanged. The repository builds and the existing `MemberConfiguration`,
`LeaderConfiguration`, `RoleOrder`, `Delivery`, and stdio UI protocol scenarios pass through the published tools.

**Status: complete (aa3b069eba).** Headquarters launch carries one ordered `SquadDefinition` from resolved
configuration into backend setup, `SquadMembers`, and handoff delivery. `PreparedLaunch` has no parallel member,
leader, or handoff-member fields. `MemberConfigRow` and `MemberConfiguration` are gone; `RoleRow` remains for
slice 2.

### Slice 2 - Replace the command-side role row with domain members [done]

**Outcome:** The role-facing CLI resolves, addresses, and validates configured participants using
`SquadMemberDefinition`, leaving no fourth member representation in `squad.Configuration`.

- Change the lenient command-side configuration reader to map member entries to
   `SquadMemberDefinition` values, including distinct member and role identities, resolved worktree data, normalized
   agent settings, and the current string receive mode. It may expose the ordered member list needed by commands; it
   must not add a command-specific participant record or manufacture a second `SquadDefinition`.
- Make `CurrentRoleResolver`, `context`, `handoff`, `ready-for-next`, and `done-with-current` consume the canonical
   member values. Compare typed identities internally and convert `SquadMemberId.Value` only at existing console,
   filesystem, Git, and handoff-document boundaries. Preserve the role-shaped command names and output because they
   are compatibility vocabulary for the addressed member.
- Preserve the current command-reader behavior for missing or malformed configuration, configured order, worktree
   path normalization, display-name and receive-mode defaults, an explicitly empty receive mode, unsupported receive
   modes, ambiguous worktrees, and recipient validation. Do not silently coerce malformed external values merely to
   construct a domain value.
- Delete `RoleRow` and rename helpers whose implementation terminology still claims that the configured
   participants are reusable roles. Do not retain a compatibility alias or wrapper.

**Acceptance:** `RoleRow`, `MemberConfigRow`, and `MemberConfiguration` are all absent, with
`SquadMemberDefinition` as their only configured-and-resolved replacement. `squad context`, handoff sender and
recipient validation, task dispatch, and batch dispatch retain their current output, exit codes, ordering, and
filesystem behavior. The repository builds and the existing `Context`, `Handoffs`, `TaskQueue`, and `BatchQueue`
features pass through the published `squad` executable.

**Status: complete (05a8cebb1b).** The lenient `SquadConfig.ReadMembers` now maps schema-version-2 member
entries directly to `SquadMemberDefinition` (member identity, role, resolved worktree data, normalized agent
settings, and the current string receive mode); `RoleKnown`/`Find` are `MemberKnown`/`Find` over that type.
`CurrentRoleResolver.Resolve` and `squad context`/`handoff`/`ready-for-next`/`done-with-current` consume it,
converting to `SquadMemberId.Value` only at console/handoff-document boundaries. `RoleRow` is deleted. `squad.json`
schema, CLI output, exit codes, and diagnostics are unchanged.

### Slice 3 - Type receive mode at its owning boundaries [done]

**Outcome:** Valid configured members carry only `ReceiveMode.Task` or `ReceiveMode.Batch` internally, while
configuration JSON and command behavior continue to use the stable `task` and `batch` spellings.

- Add the dependency-free `ReceiveMode` enum to `squad.Domain`. Change the validated
   `SquadMemberConfiguration` value and `SquadMemberDefinition.ReceiveMode` to that enum; keep the JSON document
   property as a string.
- Parse missing, `task`, `batch`, empty, and unsupported external values explicitly in the two configuration
   adapters. The strict launch loader must retain its current default and validation diagnostic. The lenient
   command-side reader must preserve its existing empty/unsupported-mode exit behavior without adding `Unknown` to
   the enum, retaining a raw string on a domain descriptor, or defaulting invalid input to `Task`.
- Dispatch `ready-for-next` and `done-with-current` by the enum. Use explicit boundary mappings rather than JSON
   attributes on the domain type, `Enum.Parse`, or casing `ToString()` output.
- Add one configuration-focused black-box scenario proving that an unsupported receive-mode token is rejected
   before a member session starts. Reuse the existing task, batch, default, and explicitly empty-mode scenarios for
   the other mappings; do not add tests that inspect the enum itself.

**Acceptance:** Every authoritative receive-mode value outside the raw JSON documents is typed as `ReceiveMode`;
the domain enum contains exactly `Task` and `Batch`. Omitted mode still means task, `batch` still selects batch
queue behavior, an empty command-side mode retains its current diagnostic and exit code, and unsupported launch
configuration is rejected with the existing `expected task or batch` diagnostic before any session starts. The
repository builds and the configuration, task-queue, and batch-queue acceptance scenarios pass through the
published executables.

**Status: complete (7a4158c6da).** `ReceiveMode` is `Task`/`Batch` only. Launch maps a validated string onto the
enum and still rejects unsupported tokens with `expected task or batch` before any session starts. Command-side
omitted mode is `Task`; empty is `Unknown role` / exit 1; unsupported is `INVALID_RECEIVE_MODE` / exit 2; `task` /
`batch` dispatch by the enum. The raw token is not stored on `SquadMemberDefinition`.

## Slice 3 review (dccf420f46) — addressed (7a4158c6da)

### Finding 1 — High

- **Location:** `src/squad.Domain/SquadMemberDefinition.cs` (`ReceiveMode?`); `src/squad.Configuration/SquadConfig.cs`
  (`ReadMembers` maps empty and unsupported tokens to `null`); `src/squad/Commands/ReadyForNext.cs` and
  `src/squad/Commands/DoneWithCurrent.cs` (every `null` is `Unknown role` / exit 1).
- **Violated behavior:** Slice 3 requires the lenient command-side reader to preserve existing empty *and*
  unsupported-mode exit behavior, without adding `Unknown` to the enum, retaining a raw string on a domain
  descriptor, or defaulting invalid input to `Task`. Empty receive mode must stay `Unknown role: {role}` / exit 1.
  A non-empty unsupported token must stay `INVALID_RECEIVE_MODE: {value} for role {role}` / exit 2. Valid `task` /
  `batch` / omitted mode still dispatch by the enum. `SquadMemberDefinition.ReceiveMode` is specified as that enum;
  every authoritative value outside raw JSON is `ReceiveMode`. That an existing scenario did not cover the
  unsupported command-side path does not license dropping it. The Acceptance paragraph's launch diagnostic does not
  override the bullet that preserves command-side unsupported-mode exit behavior.
- **Root cause:** Empty and unsupported tokens are both mapped to `null` on `ReceiveMode?`, and both commands treat
  all `null` as the empty-mode path. The offending token is discarded, so the exit-2 diagnostic cannot be produced.
  `null` is a third authoritative value on the canonical descriptor — a stand-in for `Unknown`.
- **Required outcome:** Keep both command-side diagnostics and exit codes. Parse missing, `task`, `batch`, empty, and
  unsupported values explicitly in the command-side adapter. Dispatch `ready-for-next` and `done-with-current` by
  `ReceiveMode` for `Task` and `Batch`. Do not add `Unknown`, do not store the raw token on `SquadMemberDefinition`,
  and do not default invalid input to `Task`. Leave the strict launch `expected task or batch` rejection unchanged.

**Status: complete (7a4158c6da).** Empty command-side receive mode stays `Unknown role` / exit 1; a non-empty
unsupported token stays `INVALID_RECEIVE_MODE` / exit 2. Valid modes dispatch by `ReceiveMode`. The raw token is
read only in the command-side adapter, not stored on `SquadMemberDefinition`. Launch still rejects unsupported
mode with `expected task or batch` before any session starts.

### Slice 4 - Type member status and preserve its protocol vocabulary [in progress]

**Outcome:** Mutable application state uses one closed `SquadMemberStatus` vocabulary, and the UI boundary explicitly
publishes the same lowercase status strings as before.

- Add `SquadMemberStatus` with exactly `Starting`, `Running`, `Idle`, `Stopped`, and `Error` to `squad.Domain`.
   Change `MemberAggregate`, immutable member snapshots, provider-event projection, terminal-failure handling, and
   readiness checks to use the enum; no authoritative application status string remains.
- Map every enum member explicitly when composing `state.snapshot`, preserving `starting`, `running`, `idle`,
   `stopped`, and `error` exactly. Keep the mapping at the existing application/presentation boundary; do not add
   serialization attributes or UI protocol dependencies to `squad.Domain`, and do not duplicate status rules in
   Vue.
- Finish `docs/Manual/modules.md`, `docs/Manual/architecture.md`, and `docs/Manual/glossary.md` so the documented
   shared-kernel surface is exactly `RoleId`, `SquadMemberId`, `ReceiveMode`, `SquadMemberStatus`, `AgentSettings`,
   `SquadMemberDefinition`, and `SquadDefinition`, apart from the foundational `System.Contract` utility, and so the
   mapping responsibilities of configuration, application, provider, handoff, and presentation modules are clear.
- Build and publish from clean outputs and run the complete black-box Gherkin suite through the real `squad`,
   `squad-hq`, provider, and stdio UI protocol boundaries. Do not add tests whose only purpose is proving that an old
   type or assembly name is unavailable.

**Acceptance:** Member status is typed throughout authoritative application state and the domain enum has exactly
the five required values. Existing snapshots still publish the exact lowercase strings, readiness still requires an
idle non-working member, and terminal stopped/error states remain final. The dependency-free domain surface and
manual match the required design, no displaced row records or pass-through wrappers remain, the complete solution
builds and publishes, and the full existing Gherkin suite passes without protocol, persistence, ordering, or
behavior regressions.

## Acceptance criteria

- The implemented issue-024 semantics remain unchanged.
- `squad.Domain` exists in the solution and has no project references.
- Aside from the foundational `System.Contract` guard utility, its initial product-facing surface is limited to
  `RoleId`, `SquadMemberId`, `ReceiveMode`, `SquadMemberStatus`, `AgentSettings`, `SquadMemberDefinition`, and
  `SquadDefinition`.
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