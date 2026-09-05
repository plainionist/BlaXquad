---
title: Eliminate the generic squad core assembly
priority: 20
---

The first Core split extracted transcript and handoff responsibilities into `squad.Core.Transcripts` and
`squad.Core.Handoffs`. The remaining `squad.Core` assembly is smaller, but its name still hides what it actually owns:
the in-process application model presented to the UI. It also contains `SessionRegistry`, which is host lifecycle state
and does not belong to that model.

Remove the generic `Core` boundary rather than treating it as a permanent dependency bucket.

## Current responsibility analysis

### Application model and command coordination

`SquadViewModel` is the serialized application coordinator. It implements the UI command/query contracts, owns role
state, admits commands, invokes agent sessions, receives provider events, emits snapshots and transcript updates, and
coordinates shutdown of accepted work.

Its internal modules are parts of the same authoritative state machine:

- `RoleOperations` owns per-role prompt serialization, operation cancellation, abort coordination, and event
  invalidation;
- `Interactions` owns pending permission, input, and elicitation requests and their protected transcript entries; and
- `Events` projects provider events onto role and transcript state.

These are meaningful modules but not useful independent assemblies. They collaborate through the same role state,
interaction state, transcript aggregate, and serialized commit boundary. Separating them physically would require
public mutation APIs, pass-through contracts, or competing synchronization merely to cross assembly boundaries.

### Role projection

`AgentRoleState` and `AgentRoleSnapshot` describe the current observable state of one role. They are owned by the
application model and mutated by its event projector. They are not a standalone domain package because no independent
consumer creates or advances them.

### Host session lifecycle

`SessionRegistry` records sessions started by `SquadApplication`, rejects lookup during shutdown, and is consumed by
the headquarters `SessionRoleNotifier`. Its production consumers are in `squad-hq`; it is not used by
`SquadViewModel` and is not part of the UI-facing application model.

Move it to the headquarters lifecycle module and make it internal if the tests and composition surface permit. This
also removes the duplicate impression that `SquadViewModel.RegisterSession` and `SessionRegistry.Register` are two
parts of one Core-owned session aggregate.

## Target assemblies

### `squad.Application`

Rename the remaining cohesive application-state assembly from `squad.Core` to `squad.Application`. It owns:

- `SquadViewModel`;
- `AgentRoleState` and `AgentRoleSnapshot`;
- the internal `RoleOperations` module;
- the internal `Interactions` module; and
- the internal `Events` projection module.

The assembly remains the authoritative in-process model used through `ISquadUi` and `ITranscriptUi`. It depends on
agent-provider contracts, UI contracts, and transcripts, but not on headquarters, hosting implementations, Photino,
or the Copilot SDK.

Do not create separate `Roles`, `Interactions`, `Operations`, or `Events` assemblies during this change. Their current
internal visibility protects the application model's mutation boundary and is more valuable than a larger project
count.

### `squad.Transcripts`

Rename `squad.Core.Transcripts` to `squad.Transcripts`. It already owns a complete independent subsystem:

- transcript ordering and entry indexes;
- assistant and reasoning streaming;
- tool-output progression;
- protected entries;
- retention and truncation;
- archive paging and storage; and
- transcript retention options.

The transcript assembly must not reference `squad.Application`. The application model owns its lifetime and invokes
it through its focused API.

### `squad.Handoffs`

Rename `squad.Core.Handoffs` to `squad.Handoffs`. It already owns filesystem-backed handoff delivery, polling,
recovery, and its notifier/pump contracts. It must remain independent of `squad.Application`; headquarters supplies
the adapter that wakes an application role.

### `squad-hq` lifecycle module

Move `SessionRegistry` beside `SquadApplication` and `SessionRoleNotifier` in `squad-hq`. The host starts and stops
provider sessions and therefore owns the registry's lifetime and admission phase.

Keep the registry as the single host-side session lookup authority. Do not merge it into `SquadApplication` merely to
reduce type count, and do not move provider session ownership into `squad.Application`.

## No replacement common bucket

Do not replace `squad.Core` with an empty or speculative `squad.Common` assembly. There are currently no logging or
design-by-contract primitives that justify such a project.

When concrete cross-assembly needs appear:

- prefer the standard logging abstractions unless the product has a domain-specific logging contract;
- use the BCL argument and state guards for ordinary preconditions;
- keep domain invariants with their owning aggregate; and
- introduce `squad.Foundation` only when at least two independent assemblies need the same narrow abstraction and its
  semantics cannot live in an existing standard dependency.

`squad.Foundation`, if eventually needed, must be dependency-free and contain only cross-cutting primitives. It must
not contain application models, configuration, handoff rules, transcript rules, provider contracts, or host lifecycle
state.

## Dependency direction

```text
squad-hq ----------------------> squad.Application
   |                             squad.Handoffs
   |                             hosting/provider adapters
   +-- owns SessionRegistry

squad.Application -------------> squad.Transcripts
   |                             squad.Ui.Abstractions
   +---------------------------> squad.AgentProvider.Abstractions

squad.Transcripts -------------> squad.Ui.Abstractions

squad.Handoffs ----------------> squad.Handoff
   +---------------------------> squad.Configuration

squad.Photino -----------------> squad.Ui.Abstractions
squad.CopilotSdk --------------> squad.AgentProvider.Abstractions
```

There must be no reference from transcripts or handoffs back to the application model, and no reference from the
application model to headquarters or technology adapters.

## Coordination status

Slice 1 is complete (aad01aec15). Slice 2 is complete (9a0dad4ae6). Slice 3 is complete (ae73524ae8). Slices 4-5
remain blocked until the architect authorizes the next slice.

## Implementation plan

### Slice 1: Move host session lifecycle

**Status: complete (aad01aec15)**

1. Move `SessionRegistry` from `squad.Core` to `squad-hq/Commands` and change its namespace to `squadHQ.Commands`.
   Keep it as a separate `internal sealed` lifecycle collaborator; do not fold it into `SquadApplication`.
2. Update `SquadApplication`, `SessionRoleNotifier`, and `Launch` so headquarters remains the only production assembly
   that creates or consumes the registry. Remove the registry from the public `SquadApplication` constructor surface;
   use an internal injection overload or equivalent internal composition seam when the shared instance is required.
3. Grant `squad.Specs` narrowly scoped internal access to `squad-hq` so the existing black-box fixture can compose the
   same registry into `SquadApplication` and `SessionRoleNotifier`. Do not make the registry public solely for tests.
4. Remove `SessionRegistry` and its namespace imports from `squad.Core`, while retaining the `squad.Core` project and
   all other application-model types for Slice 4.
5. Preserve registration, missing-session and completed-session rejection, shutdown admission, and handoff
   notification behavior. Keep `SessionRegistry` as the only host-side session lookup authority; the
   `SquadApplication.Sessions` projection is not a replacement lookup API.

Slice 1 is accepted when:

- `SessionRegistry` is internal to `squad-hq` and `squad.Core` no longer defines or owns host lifecycle state;
- production composition shares one registry between `SquadApplication` and `SessionRoleNotifier`;
- the agent CLI architecture boundary still excludes `SessionRegistry` and all headquarters types; and
- focused startup, shutdown, and in-process handoff notification scenarios pass without changing their observable
  behavior.

### Slice 2: Rename transcripts

**Status: complete (9a0dad4ae6)**

1. Rename the `src/squad.Core.Transcripts` directory, project file, assembly, and root namespace to
   `src/squad.Transcripts`, `squad.Transcripts.csproj`, and `squad.Transcripts`.
2. Update solution membership and every production and test project reference. Update namespace imports in the
   application model and specs without moving transcript behavior or types into either consumer.
3. Update architecture assertions and supported architecture/glossary documentation to name `squad.Transcripts`.
   Historical issue text may retain the old name where it describes the pre-change state.
4. Preserve dependency direction: `squad.Transcripts` may depend on `squad.Ui.Abstractions`, but it must not reference
   `squad.Core`, headquarters, handoff delivery, hosting/provider implementations, Photino, or the Copilot SDK.
5. Do not rename `squad.Core`, `squad.Core.Handoffs`, or their namespaces in this slice.

Slice 2 is accepted when:

- the solution builds the transcript subsystem only as `squad.Transcripts`, with no stale
  `squad.Core.Transcripts` project or assembly in the supported source/build graph;
- transcript ordering, streaming, protected-entry, retention, truncation, and archive paging behavior is unchanged;
- architecture coverage proves the transcript assembly cannot reference the application model or technology adapters;
  and
- focused transcript scenarios pass.

### Slice 3: Rename handoffs

**Status: complete (ae73524ae8)**

1. Rename the `src/squad.Core.Handoffs` directory, project file, assembly, and root namespace to
   `src/squad.Handoffs`, `squad.Handoffs.csproj`, and `squad.Handoffs`.
2. Update solution membership, the headquarters project reference and imports, and every specs project reference and
   import. Do not move delivery, polling, recovery, notifier, or pump responsibilities into headquarters.
3. Update architecture assertions and supported architecture/glossary documentation to name `squad.Handoffs`.
   Historical issue text may retain the old name where it describes the pre-change state.
4. Preserve dependency direction: `squad.Handoffs` may depend on `squad.Handoff` and `squad.Configuration`, but it must
   not reference `squad.Core`, `squad.Transcripts`, headquarters, hosting/provider implementations, Photino, or the
   Copilot SDK.
5. Keep `IRoleNotifier` as the narrow inward notification contract owned by `squad.Handoffs`.
   `SessionRoleNotifier` remains the headquarters adapter that reaches `SquadViewModel`; do not introduce a direct
   handoff-to-application reference.
6. Do not rename `squad.Core` or its namespaces in this slice.

Slice 3 is accepted when:

- the solution builds the handoff-delivery subsystem only as `squad.Handoffs`, with no stale
  `squad.Core.Handoffs` project or assembly in the supported source/build graph;
- filesystem delivery, polling, recovery, failure reporting, and role notification behavior is unchanged;
- architecture coverage proves handoff delivery reaches application behavior only through `IRoleNotifier` and cannot
  reference the application model or technology adapters; and
- focused handoff delivery, recovery, and notification scenarios pass.

### Slice 4: Replace Core with Application

1. Rename `squad.Core` to `squad.Application` and update its root and internal namespaces.
2. Keep `SquadViewModel`, role state, role operations, interactions, and event projection together.
3. Update headquarters, Photino composition, acceptance tests, architecture rules, and documentation.
4. Remove all `squad.Core*` projects, namespaces, assembly references, and stale published artifacts from the supported
   source and build graph.

### Slice 5: Verify boundaries

1. Add architecture assertions for the target assembly graph and the absence of `squad.Core*` projects.
2. Verify that the `squad` agent CLI still cannot reach application, transcript, handoff-delivery, host, provider
   runtime, or UI assemblies.
3. Run focused role command, interaction, transcript, handoff, startup, relaunch, and shutdown scenarios, then the full
   build and acceptance suite.

## Acceptance criteria

- No project, assembly, namespace, solution entry, or supported documentation remains named `squad.Core` or
  `squad.Core.*`.
- `squad.Application` has one cohesive responsibility: the authoritative in-process application model and serialized
  command/event coordination exposed to the UI.
- `RoleOperations`, `Interactions`, and `Events` remain internal implementation modules of `squad.Application`.
- `SessionRegistry` is owned by the headquarters lifecycle module and has no dependency on the application assembly.
- `squad.Transcripts` and `squad.Handoffs` remain independently testable and cannot reference
  `squad.Application`.
- There remains exactly one serialized application mutation boundary and one host session registry.
- No speculative common/foundation assembly is introduced.
- Existing public UI, transcript, provider, and handoff behavior remains unchanged.
- Existing black-box Gherkin scenarios and the full .NET build/test suite remain green.

## Relationship to existing issues

This issue follows the completed extraction of transcript and handoff assemblies. It complements
`020 refactor squad application.md` and `040 refactor session registry.md`: those issues govern lifecycle internals,
while this issue assigns the resulting types to explicit assemblies and removes the generic Core category.