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

Issue 027 consolidates the Headquarters runtime and has the preceding priority. Complete that structural change
before this one so project, solution, packaging, and module-documentation edits do not overlap unnecessarily.

Issue 024 will establish squad-member identity, member-local state ownership, and replaceable squad generations.
This issue is a smaller assembly-boundary correction and must not preempt that domain redesign. It may rename only
the transcript namespace required by the folder move; role-to-member naming and ownership changes remain in issue
024.

## Implementation plan

This change is small enough for one buildable commit:

1. Move the five transcript source files under `squad.Application/Transcripts`, update their namespaces and imports,
   and internalize implementation-only types.
2. Remove the transcript project reference, project file, directory, and solution entry.
3. Update `docs/Manual/modules.md` so `squad.Application` explicitly owns transcript projection, retention, and
   archive access, and remove the standalone `squad.Transcripts` module entry. Update other stable manual text only
   where it names the removed assembly.
4. Build and publish from clean outputs. Confirm the published Headquarters contains `squad.Application.dll` and no
   `squad.Transcripts.dll`.
5. Run the existing black-box Gherkin coverage for transcript projection, streaming finalization, tool activity,
   retention, paging, synchronization ordering, interaction protection, and Headquarters cleanup.

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