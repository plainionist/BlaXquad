---
title: Merge transcript implementation into application
priority: 6
---

# Merge transcript implementation into application

## Problem

The `squad.Transcripts` assembly has one source-level consumer: `squad.Application`. It has no independent
deployment, plug-in contract, alternate implementation, or supported library consumer. The acceptance suite
observes transcript behavior through the application and UI protocol rather than referencing the transcript
implementation directly.

The runtime ownership also crosses the assembly boundary in both directions conceptually:

- `SquadViewModel` constructs and disposes the shared `TranscriptArchive`;
- `AgentRoleState` constructs each `RoleTranscriptState`;
- application state supplies the synchronization object used by `RoleTranscriptState`, so role and transcript
  mutations can commit atomically; and
- `AgentEventProjector` translates provider events and directly drives transcript transitions.

Consequently, the transcript module does not own an independent lifecycle or synchronization boundary. The separate
assembly instead forces `RoleTranscriptState`, `TranscriptArchive`, `TranscriptRetentionOptions`, and
`ToolCompletionResult` to be public even though they are application implementation details.

The assembly was extracted in commit `a72bc76` to enforce a narrow dependency direction: transcript code references
only `squad.Ui.Abstractions` and does not depend on the broader application or provider model. That remains a useful
internal design rule, but it does not require a separately shipped DLL. Folder and namespace structure can preserve
the responsibility while removing an assembly boundary that suggests more independence than the implementation has.

## Goal

Move the transcript implementation into `squad.Application` as a cohesive `Transcripts` component. Remove the
standalone project, project-reference edge, redundant public API, and published `squad.Transcripts.dll` without
changing transcript behavior, UI contracts, retention limits, archive lifetime, or synchronization semantics.

The resulting structure should make ownership explicit:

```text
src/squad.Application/
    Transcripts/
        RoleTranscriptState.cs
        ToolCompletionResult.cs
        TranscriptArchive.cs
        TranscriptEntryBuffer.cs
        TranscriptRetentionOptions.cs
    AgentRoleState.cs
    SquadViewModel.cs
    ...
```

An assembly may contain multiple cohesive components. Consolidating projects must not flatten transcript retention,
streaming, correlation, and archive I/O into `SquadViewModel` or `AgentRoleState`.

## Required design

### Merge the project

Move every source file from `src/squad.Transcripts` into `src/squad.Application/Transcripts` and align namespaces
with the folder:

```csharp
namespace squad.Application.Transcripts;
```

Remove:

- `src/squad.Transcripts/squad.Transcripts.csproj`;
- the `squad.Transcripts` entry from `squad.slnx`; and
- the `squad.Application` project reference to `squad.Transcripts`.

Do not leave a compatibility assembly, type forwarders, linked duplicate sources, namespace wrappers, or deprecated
aliases. These types are not a supported external API.

### Internalize the implementation surface

Make transcript implementation types internal where no assembly-crossing caller remains. This includes
`RoleTranscriptState`, `TranscriptArchive`, `TranscriptRetentionOptions`, and `ToolCompletionResult`;
`TranscriptEntryBuffer` is already internal.

Keep transcript data exchanged with UI protocol and presentation clients in `squad.Ui.Abstractions`. Types such as
`TranscriptEntry`, `TranscriptUpdate`, `RoleTranscriptSnapshot`, `RoleTranscriptPage`, and
`RoleArchivedTranscriptEntry` are cross-module contracts and must not move into the application implementation.

### Preserve the component boundary

Keep the current responsibilities in focused transcript classes:

- `RoleTranscriptState` owns ordered per-role entries, stream assembly, tool-call correlation, live retention, and
  archive access;
- `TranscriptArchive` owns private temporary storage, paging, reconstruction, and archive retention;
- `TranscriptEntryBuffer` owns bounded streamed-entry accumulation; and
- `TranscriptRetentionOptions` owns the related limits.

`AgentEventProjector` remains the translation boundary from provider events into transcript operations. Code in the
`Transcripts` folder must continue to operate on normalized transcript values and UI contract types rather than
depending directly on provider event types or session APIs. The merge grants compile-time access to more application
dependencies; it must not turn that access into coupling.

### Preserve ownership and behavior

Retain the existing application-owned lifecycle and synchronization invariants:

- one archive is created for a `SquadViewModel` and disposed with it;
- each role transcript uses the same synchronization boundary as its corresponding `AgentRoleState`;
- role-state and transcript updates remain atomically observable;
- protected tool and interaction entries remain protected until their existing completion paths release them;
- live and archived retention limits and truncation markers remain unchanged;
- archive storage remains private, temporary, and removed during disposal; and
- transcript snapshots, incremental updates, synchronization, pages, archived-entry reads, and sequences remain
  protocol-compatible.

Do not use this project merge to redesign locking, introduce another facade, or move transcript ownership out of the
application model.

## Relationship to other issues

Issue 027 completed the preceding Headquarters runtime consolidation, so this issue can now change project, solution,
packaging, and module documentation without overlapping that work.

Issue 024 will establish squad-member identity, member-local state ownership, and replaceable squad generations.
This issue is a smaller assembly-boundary correction and must not preempt that domain redesign. It may rename only
the transcript namespace required by the folder move; role-to-member naming and ownership changes remain in issue
024.

## Implementation plan

Exactly one slice is required. The source move, namespace and visibility changes, project removal, documentation,
and behavior verification establish one assembly-boundary claim. Splitting them would leave either a duplicate
implementation, a broken project graph, or documentation that describes an intermediate structure.

### Slice 1: Fold transcript implementation into `squad.Application` [in progress]

**Outcome:** `squad.Application` contains a cohesive internal `Transcripts` component, and a clean Headquarters
publication no longer contains `squad.Transcripts.dll`, while transcript protocol behavior, retention, archive
lifetime, and synchronization remain unchanged.

1. Move `RoleTranscriptState.cs`, `ToolCompletionResult.cs`, `TranscriptArchive.cs`,
   `TranscriptEntryBuffer.cs`, and `TranscriptRetentionOptions.cs` to
   `src/squad.Application/Transcripts`. Change their namespace to `squad.Application.Transcripts`, update the two
   application imports, and make the four currently public implementation types internal. Keep the UI-facing
   transcript DTOs in `squad.Ui.Abstractions`.
2. Preserve the component boundary during the move: keep retention, stream assembly, tool correlation, and archive
   I/O in the moved transcript classes; keep provider-event translation in `AgentEventProjector`; and do not add
   provider or session dependencies to the `Transcripts` folder.
3. Remove the `squad.Application` project reference to `squad.Transcripts`, delete
   `src/squad.Transcripts/squad.Transcripts.csproj`, remove its `squad.slnx` entry, and remove the obsolete source
   directory. Do not add a compatibility assembly, forwarded types, duplicate linked sources, wrappers, or aliases.
4. Update `docs/Manual/modules.md` so the `squad.Application` entry explicitly owns transcript projection, bounded
   live state, temporary archive storage, paging, and reconstruction, then remove the standalone
   `squad.Transcripts` entry. Change other stable manual text only if it names the removed assembly.
5. Preserve the existing single-archive `SquadViewModel` lifetime, per-role shared synchronization object, atomic
   role/transcript observation, protected-entry completion paths, sequence behavior, retention and truncation
   limits, private temporary storage, paging, and disposal cleanup.
6. Run the existing black-box Gherkin coverage in the `Transcript*.feature` files together with interaction
   protection and clean-shutdown archive removal. Add or change a scenario only if implementation exposes an
   unsupported observable gap; do not test namespaces, visibility, project references, or the absence of an API.
7. From cleaned outputs, build the solution and publish `squad-hq`. Confirm the publish directory and dependency
   manifest contain `squad.Application.dll` and no `squad.Transcripts.dll`, search tracked source, project,
   solution, and stable documentation files for obsolete `squad.Transcripts` references, then run the full backend
   Gherkin suite and product build.

No new scenario should be added merely to prove that the removed assembly is unavailable. Add or change a Gherkin
scenario only if implementation reveals an observable behavior not already protected by the existing suite.

## Acceptance criteria

- Transcript implementation lives under `src/squad.Application/Transcripts` in the
  `squad.Application.Transcripts` namespace.
- `squad.Transcripts.csproj`, its solution entry, and all project references to it are removed.
- No source file references the `squad.Transcripts` namespace or assembly.
- Transcript implementation types that no longer cross an assembly boundary are internal.
- UI-facing transcript DTOs remain in `squad.Ui.Abstractions` with unchanged serialized behavior.
- Transcript logic remains structurally separate from `SquadViewModel`, `AgentRoleState`, and provider-event
  translation.
- Archive ownership, cleanup, synchronization, streaming, tool correlation, retention, truncation, paging, and
  ordering behavior remain unchanged.
- Stable module documentation describes transcript behavior as part of `squad.Application` and no longer lists a
  standalone transcript module.
- A clean build and publish do not produce or package `squad.Transcripts.dll`.
- The focused transcript and cleanup Gherkin scenarios pass through the application and UI protocol, followed by the
  full backend Gherkin suite and product build.

## Non-goals

- Changing transcript protocol messages, DTOs, sequence semantics, or dashboard reconciliation.
- Changing retention limits, truncation rules, archive format, archive durability, or cleanup timing.
- Folding transcript classes into one large application class.
- Redesigning application synchronization, command serialization, or provider-event projection.
- Implementing the squad-member aggregate or restartable squad lifetime from issue 024.
- Creating a replacement transcript abstraction or interface for hypothetical future consumers.