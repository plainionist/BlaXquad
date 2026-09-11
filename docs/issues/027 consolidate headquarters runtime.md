---
title: Consolidate headquarters runtime
priority: 5
---

# Consolidate headquarters runtime

## Problem

Headquarters runtime behavior is split between `squad.Host.Runtime` and `squad.Host.Control`:

- `squad.Host.Runtime` coordinates startup, provider-session generations, command admission, handoff delivery,
  termination races, and ordered cleanup.
- `squad.Host.Control` owns the per-project process lease, lock and metadata, local named-pipe protocol, shutdown and
  readiness client, stale-state cleanup, and one CLI-only project-root resolver.

The components are conceptually distinct, but the assembly boundary no longer protects an independent deployment,
consumer, or implementation choice:

- both assemblies are used and shipped only by `squad-hq`;
- `squad.Host.Runtime` already references `squad.Host.Control` and directly owns a `HostLease`;
- `squad-hq` references both projects, using the control client only for its own `shutdown` and `wait-for-agent`
  commands;
- no other executable, plug-in, or supported library consumes the control assembly;
- the acceptance suite exercises ownership and control through the published `squad-hq` process rather than by
  referencing either assembly; and
- the split was introduced by commit `41177a9` as one slice of a broad assembly extraction, not because control had
  a separate packaging or replacement requirement.

The current names also overload "host". In this repository, `squad.Hosting.*` denotes machine/window/platform
integration such as Photino and sleep inhibition. `squad.Host.Runtime` instead means the long-running Headquarters
application. A reader can reasonably interpret "host" as the machine or operating-system boundary, making the
current module family ambiguous.

One assembly does not require one class or one undifferentiated responsibility. The named-pipe protocol and lease
mechanics should remain isolated behind a narrow API, but they do not need a separate DLL to remain a separate
component.

## Goal

Merge `squad.Host.Runtime` and `squad.Host.Control` into one `squad.Runtime` assembly that owns the complete
Headquarters process runtime:

- project ownership and local process control;
- startup, running, and shutdown coordination;
- provider-session generations and command admission;
- runtime failure observation;
- handoff, UI-host, sleep, and provider resource lifecycle; and
- ordered, failure-safe cleanup.

Keep control protocol code structurally separate from lifecycle/session code inside the module. Remove the redundant
project boundary, public surface, dependency edge, and "Host.Runtime" terminology without changing supported
behavior or persisted runtime state.

## Required design

### Create one `squad.Runtime` module

Rename `src/squad.Host.Runtime` and its project to:

```text
src/squad.Runtime/
    squad.Runtime.csproj
```

Move the sources from `squad.Host.Control` into a `Control` responsibility folder in that project. A representative
layout is:

```text
src/squad.Runtime/
    Control/
        HeadquartersControlClient.cs
        HeadquartersControlRequest.cs
        HeadquartersLease.cs
        HeadquartersCleanupLease.cs
    SessionGeneration.cs
    SessionRoleNotifier.cs
    SquadApplication.cs
    SquadRuntimeController.cs
    ...
```

Namespaces must follow the folders:

```csharp
namespace squad.Runtime;
namespace squad.Runtime.Control;
```

The exact internal control-helper names may vary, but do not flatten pipe, lock, protocol parsing, and stale-state
cleanup into `SquadApplication`. Assembly consolidation removes a project boundary; it does not transfer every
responsibility into the lifecycle coordinator.

Remove both old projects from `squad.slnx`, remove their project-reference edges, and add one reference from
`squad-hq` to `squad.Runtime`. Do not leave compatibility projects, type forwarders, duplicate source links, old
namespace wrappers, or deprecated aliases. These are internal application assemblies, not a supported library API.

### Use Headquarters terminology for process control

Where "host" means the running `squad-hq` process rather than platform hosting, prefer the established domain term
"Headquarters". Rename the assembly-crossing control types accordingly, including:

- `HostLease` to `HeadquartersLease`;
- `HostControlClient` to `HeadquartersControlClient`; and
- `HostControlRequest` to `HeadquartersControlRequest`.

Rename `CleanupLease` consistently if it remains a distinct type. Keep internal helpers internal. Do not rename
provider `IAgentRuntime` concepts or the `squad.Hosting.*` family; those refer to different boundaries.

Update user-facing diagnostics and manual prose from ambiguous phrases such as "squad host" or "host-control" to
"Headquarters" or "Headquarters control" where that does not name a persisted compatibility artifact. CLI command
names remain `squad-hq launch`, `shutdown`, and `wait-for-agent`.

### Keep the control component narrow

The control folder continues to own:

- normalized per-project ownership;
- the exclusive lock lifetime;
- atomic publication and removal of process metadata;
- deterministic named-pipe identity;
- versioned request parsing and response serialization;
- shutdown and agent-readiness requests;
- stale metadata cleanup guarded by lock acquisition; and
- the short-lived client used by Headquarters management commands.

`SquadApplication` continues to coordinate the lease as an owned runtime resource and observe its shutdown and
server-failure signals. Preserve the narrow readiness-provider bridge established by issue 025; do not use this merge
to reach directly into application collections from the pipe server.

The C4 architecture may continue to show Headquarters control and lifecycle as separate components. A component
boundary does not imply a one-component-per-assembly rule.

### Rehome the CLI-only project-root resolver

`HostProjectRoot` is used only by the `squad-hq wait-for-agent` command and invokes Git through `squad.Process`. Move
that behavior into the command/composition side, with a name such as `ProjectRootResolver`, rather than carrying a
one-caller CLI helper into `squad.Runtime.Control`.

After that move, do not make `squad.Runtime` depend on `squad.Process` solely to throw `CliExitException` from lease
acquisition. Expected ownership contention should use an explicit acquisition result, and `squad-hq` should
translate that result into the existing exit code and diagnostic at the command boundary. Do not add a custom
exception when the caller only needs to distinguish acquired from already owned.

### Preserve persisted and wire compatibility

The source and terminology migration must not gratuitously change the local coordination format. Preserve:

- `.blaxquad/host.lock`;
- `.blaxquad/host.json` and its fields;
- the deterministic `blaxquad-*` pipe identity;
- control protocol version `1`;
- `ping`, `shutdown`, and `agent-status` command names; and
- existing stale-lock and stale-metadata recovery behavior.

Those identifiers are persisted or cross-process protocol data, not C# module names. Renaming them would require an
explicit compatibility and migration design and is outside this issue.

### Preserve lifecycle ownership and ordering

The merged module must retain these invariants:

- project ownership is acquired before workspace preparation or provider startup can mutate runtime state;
- a duplicate launch fails without disturbing the live process or its metadata;
- the control endpoint is reachable early enough for shutdown to win while startup is still pending;
- readiness reports initializing, ready, not-ready, and unknown-role with the existing semantics;
- shutdown waits for project ownership to be released;
- a control-server failure remains a terminal runtime signal;
- stale metadata is removed only while holding cleanup ownership; and
- cleanup stops runtime work before disposing the control endpoint and releasing the project lock.

If lease acquisition moves behind a `squad.Runtime` creation API to reduce public surface, that API must preserve
the same early-acquisition and failure-cleanup guarantees. Do not add a factory or facade whose only purpose is to
hide a constructor while retaining the same public collaborators.

## Relationship to other issues

Issues 025 and 026 are complete. Their resulting architecture is the baseline for this work: keep the single
application session/admission authority established by issue 025 and keep the runtime-loaded `squad.Hosting.*`
family established by issue 026. Both completed issue documents have been deleted, so there are no stale issue files
to update and no other open issue owns part of this merge.

## Implementation plan

The existing black-box suite already protects the externally triggerable behavior in `HostOwnership.feature`,
`HostCoexistence.feature`, `HeadquartersLifecycle.feature`, `HeadquartersEarlyShutdown.feature`,
`HeadquartersCleanupDiagnostics.feature`, `HeadquartersPartialStartupFailure.feature`, and
`HeadquartersTermination.feature`. A named-pipe listener construction failure has no deterministic supported
process-level trigger. Preserve its terminal signal and its place in the runtime race exactly; do not introduce a
friend assembly, reflection, injectable product seam, protocol command, or test-only production branch merely to
force that internal failure.

Every slice below is an independently buildable and integratable commit. Its documentation and focused acceptance
coverage must describe the repository state produced by that slice; no slice may depend on a later cleanup to restore
correctness.

### Slice 1: Rename the lifecycle assembly to `squad.Runtime` [in progress]

**Outcome:** Headquarters lifecycle code is built and published as `squad.Runtime`, while the unchanged
`squad.Host.Control` assembly continues to provide process control.

1. Rename `src/squad.Host.Runtime` and `squad.Host.Runtime.csproj` to
   `src/squad.Runtime/squad.Runtime.csproj`, change its lifecycle namespaces to `squad.Runtime`, and update all
   production call sites.
2. Replace only the runtime solution and `squad-hq` project references. Keep `squad.Host.Control` as the runtime
   project's existing dependency and do not move or rename any control source or type in this slice.
3. Update `docs/Manual/modules.md` and any directly affected architecture text to describe the valid intermediate
   graph: `squad.Runtime` owns lifecycle coordination and still depends on the separate `squad.Host.Control` module.
4. Build and publish from clean outputs. Confirm `squad.Runtime.dll` replaces `squad.Host.Runtime.dll`, while
   `squad.Host.Control.dll` remains present, and run the focused healthy lifecycle and termination scenarios.

### Slice 2: Remove CLI presentation from `squad.Host.Control` [pending]

**Outcome:** The still-separate control module owns only process-control mechanics and no longer depends on
`squad.Process` for command presentation or Git-based CLI project discovery.

1. Delete `HostProjectRoot`. At the `WaitForAgent` command boundary, compose
   `squad.Configuration.ProjectRoot.ResolveViaGit` and `ResolveProjectRoot` so main-checkout, linked-worktree, explicit
   root, validation-order, and missing-project behavior remain unchanged.
2. Replace the lease's `CliExitException` with an explicit `TryAcquire` result for expected lock contention.
   Translate only an unsuccessful acquisition to the existing exit code and duplicate-launch diagnostic in `Launch`;
   let unexpected acquisition failures remain exceptions rather than mapping them to "already running".
3. Remove the `squad.Process` project reference from `squad.Host.Control`. Keep all module, namespace, and existing
   Host terminology unchanged so this slice contains only the dependency and presentation-boundary correction.
4. Run the focused duplicate-launch, main-checkout discovery, linked-worktree discovery, missing-project, equivalent
   path, stale-state recovery, and shutdown scenarios, then build the solution.

### Slice 3: Merge process control into `squad.Runtime` [pending]

**Outcome:** A published `squad-hq` uses one `squad.Runtime` assembly for lifecycle and process control, with the
control component remaining structurally separate.

1. Move the remaining `squad.Host.Control` sources into `src/squad.Runtime/Control`, change their namespace to
   `squad.Runtime.Control`, and update lifecycle and `squad-hq` call sites. Keep the existing `Host*` type names and
   operator diagnostics in this slice.
2. Remove `squad.Host.Control.csproj`, its solution entry, the runtime-to-control project edge, and the second
   `squad-hq` reference. Keep request parsing, pipe serving, lock/metadata ownership, stale cleanup, and client access
   in dedicated control types rather than folding them into `SquadApplication`.
3. Internalize only helpers whose cross-assembly visibility became unnecessary; keep the lease and client public
   because `squad-hq` consumes them.
4. Update `docs/Manual/modules.md` and architecture descriptions to show one runtime module with distinct lifecycle
   and control components. Add or extend an architecture-fitness acceptance scenario that proves a clean published
   `squad-hq` contains `squad.Runtime.dll` and no `squad.Host.Control.dll`.
5. Run the ownership, coexistence, lifecycle, early-shutdown, readiness, startup-failure, termination, and cleanup
   scenarios. Build and publish from clean outputs and confirm the merged project has no `squad.Process` dependency.

### Slice 4: Adopt Headquarters process-control terminology [pending]

**Outcome:** Source, diagnostics, specifications, and manuals consistently call the running process
Headquarters while persisted and wire compatibility remains unchanged.

1. Rename `HostLease`, `HostControlClient`, `HostControlRequest`, and `CleanupLease` to their cohesive
   `Headquarters*` names. Rename corresponding source fields, locals, test-support identifiers, feature names,
   bindings, comments, and XML documentation that refer to the running process.
2. Update operator diagnostics and Gherkin/manual prose from "squad host" and "host-control" to Headquarters terms.
   Update `docs/Manual/architecture.md`, `modules.md`, and `test-strategy.md` in the same commit.
3. Do not rename `.blaxquad/host.lock`, `.blaxquad/host.json` or its schema, the `blaxquad-*` pipe identity, protocol
   version `1`, `ping`, `shutdown`, or `agent-status`. Do not rename provider `IAgentRuntime`, generic window-hosting
   concepts, or any `squad.Hosting.*` module.
4. Run the full backend Gherkin suite and product build. Search tracked source, project/solution files,
   documentation, scripts, and generated-spec inputs for obsolete assembly, namespace, and process-control terms,
   allowing only persisted/wire compatibility names and historical text in this issue. Confirm the final clean
   publish contains `squad.Runtime.dll` exactly once and neither old assembly.

## Acceptance criteria

- `src/squad.Runtime/squad.Runtime.csproj` is the sole Headquarters runtime project.
- `squad.Host.Runtime.csproj` and `squad.Host.Control.csproj` are removed from disk and `squad.slnx`.
- No source or project file references the `squad.Host.Runtime` or `squad.Host.Control` namespaces or assemblies.
- `squad-hq` references `squad.Runtime` once and continues to own command parsing and CLI presentation.
- Control code remains in a dedicated `Control` folder/namespace and is not folded into `SquadApplication`.
- `HostProjectRoot` no longer exists in the runtime module; its one CLI responsibility is owned by `squad-hq`.
- Public types use Headquarters terminology where they refer to the running process, while `squad.Hosting.*` remains
  reserved for platform/window hosting.
- A clean build and publish produce `squad.Runtime.dll` and do not produce either old assembly.
- Existing `.blaxquad/host.lock`, `.blaxquad/host.json`, named-pipe identity, and version-1 control messages remain
  compatible.
- Duplicate launch protection, stale-state recovery, equivalent-root identity, independent-project coexistence,
  early shutdown, readiness polling, normal shutdown, and cleanup behavior remain covered through the real
  `squad-hq` process.
- A control-server failure remains a terminal runtime signal without introducing a test-only way to trigger it.
- The manuals describe the resulting module and types without preserving obsolete compatibility wrappers.
- The full backend Gherkin suite and product build pass. No friend assembly, reflection access, direct product-object
  test, or test-only production branch is introduced.

## Non-goals

- Combining named-pipe, lock, or metadata logic directly into `SquadApplication`.
- Changing the control wire protocol or persisted host-state filenames.
- Moving `shutdown` or `wait-for-agent` command presentation out of `squad-hq`.
- Redesigning session admission, startup planning, or handoff construction established by issue 025.
- Renaming `squad.Hosting.Abstractions` or the runtime-loaded hosting implementations from issue 026.
- Creating a reusable control package for hypothetical consumers that do not exist.
