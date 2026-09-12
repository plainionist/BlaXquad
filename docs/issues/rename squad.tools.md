---
title: rename squad.tools
priority: 1
---

these tools are not for the squad but for the head quarter
therefore rename to "squad.HeadquarterTools"
adjust namespace accordingly

## Intent

Rename the Headquarters-owned tools module from `squad.Tools` to `squad.HeadquarterTools`. The module remains the
owner of workspace issue-catalog access and the optional Git-history launcher used by `squad-hq`; this is an
architectural identity change, not a behavior, configuration, or protocol change.

## Implementation plan

### Slice 1: Rename the Headquarters tools module

1. Rename `src/squad.Tools` and `squad.Tools.csproj` to `src/squad.HeadquarterTools` and
   `squad.HeadquarterTools.csproj`, so clean builds emit `squad.HeadquarterTools.dll`.
2. Rename the `squad.Tools.Issues` and `squad.Tools.History` namespaces to
   `squad.HeadquarterTools.Issues` and `squad.HeadquarterTools.History`, and update the `squad-hq` composition root
   imports without moving or changing either responsibility.
3. Update the solution entry and the `squad-hq` project reference to the new project identity. Do not retain an old
   project, assembly, namespace alias, or compatibility wrapper.
4. Update `docs/Manual/modules.md` so the module heading and cross-reference use `squad.HeadquarterTools` and still
   describe its Headquarters-owned responsibilities accurately.
5. Build from clean outputs and run the existing black-box `IssueCatalogProtocol` and `WorkspaceToolsProtocol`
   acceptance coverage to prove that the renamed assembly is packaged and both tool paths remain unchanged.

## Acceptance criteria

- The solution contains `src/squad.HeadquarterTools/squad.HeadquarterTools.csproj` and no `squad.Tools` project.
- A clean build and publish of `squad-hq` consume and emit `squad.HeadquarterTools.dll`; no stale
  `squad.Tools.dll` is required or emitted.
- Issue-catalog types use the `squad.HeadquarterTools.Issues` namespace, Git-history types use
  `squad.HeadquarterTools.History`, and `squad-hq` composes both through those namespaces.
- No tracked solution, project, C# source, or stable module-documentation reference retains the old `squad.Tools`
  identity after this issue is closed.
- Existing issue discovery, frontmatter parsing, ordering, preview, and protocol behavior remains unchanged.
- Existing Git-history availability detection and detached launch behavior remains unchanged.
- `docs/Manual/modules.md` identifies `squad.HeadquarterTools` as the owner of both responsibilities.
- The relevant black-box Gherkin scenarios pass through the real published Headquarters boundary.

## Non-goals

- Moving issue-catalog or Git-history responsibilities to another module.
- Changing tool configuration, process-launch behavior, UI protocol messages, or public user-visible behavior.
- Renaming the `squad-tools` test publication directory, which is a bundle of executables and plug-ins rather than
  the `squad.Tools` module identity.
