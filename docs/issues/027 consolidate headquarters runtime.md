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
server-failure signals. Preserve the narrow readiness-provider bridge until issue 025 decides the final session and
admission ownership; do not use this merge to reach directly into application collections from the pipe server.

The C4 architecture may continue to show Headquarters control and lifecycle as separate components. A component
boundary does not imply a one-component-per-assembly rule.

### Rehome the CLI-only project-root resolver

`HostProjectRoot` is used only by the `squad-hq wait-for-agent` command and invokes Git through `squad.Process`. Move
that behavior into the command/composition side, with a name such as `ProjectRootResolver`, rather than carrying a
one-caller CLI helper into `squad.Runtime.Control`.

After that move, do not make `squad.Runtime` depend on `squad.Process` solely to throw `CliExitException` from lease
acquisition. Lease acquisition should report a typed runtime/control failure or another presentation-neutral
failure, and `squad-hq` should translate it into the existing exit code and diagnostic at the command boundary.
Reuse one cohesive exception rather than adding one type per lock or metadata failure.

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

Exactly one slice is required. The project move, CLI boundary adjustment, behavior protection, obsolete-name cleanup,
and directly affected documentation all serve one acceptance claim and must not be split into mechanical,
characterization, or documentation-only handoffs.

The existing black-box suite already protects the externally triggerable behavior in `HostOwnership.feature`,
`HostCoexistence.feature`, `HeadquartersLifecycle.feature`, `HeadquartersEarlyShutdown.feature`,
`HeadquartersCleanupDiagnostics.feature`, `HeadquartersPartialStartupFailure.feature`, and
`HeadquartersTermination.feature`. A named-pipe listener construction failure has no deterministic supported
process-level trigger. Preserve its terminal signal and its place in the runtime race exactly; do not introduce a
friend assembly, reflection, injectable product seam, protocol command, or test-only production branch merely to
force that internal failure.

### Slice 1: Consolidate the Headquarters runtime without changing process behavior [in progress]

**Outcome:** A published `squad-hq` contains one `squad.Runtime` assembly while launch ownership, readiness, shutdown,
failure propagation, and cleanup remain compatible through the existing Headquarters commands and persisted control
state.

1. Rename `src/squad.Host.Runtime` and its project to `src/squad.Runtime/squad.Runtime.csproj`. Move every
   `squad.Host.Control` source into `src/squad.Runtime/Control`, remove the old control project, replace both solution
   entries with one `squad.Runtime` entry, and replace the two `squad-hq` references with one runtime reference.
   Preserve all existing runtime dependency edges except the obsolete control edge; the merged project must not gain
   a `squad.Process` dependency.
2. Change lifecycle namespaces to `squad.Runtime` and control namespaces to `squad.Runtime.Control`. Rename
   `HostLease`, `HostControlClient`, `HostControlRequest`, and the distinct cleanup lease to their
   `Headquarters*` forms. Keep request parsing, pipe serving, lock/metadata work, and stale cleanup in dedicated
   control types; do not move that logic into `SquadApplication`. Make implementation helpers internal and expose
   only the types needed by the separate `squad-hq` composition assembly.
3. Replace the lease's `CliExitException` dependency with one presentation-neutral
   `HeadquartersControlException`, preserving the original cause. Translate that exception to the existing exit code
   and duplicate-launch diagnostic in `Launch`; do not broaden the catch to unrelated runtime failures. Keep lease
   acquisition before workspace preparation or provider startup, and preserve transfer/disposal ownership if
   composition fails.
4. Delete `HostProjectRoot`. At the `WaitForAgent` command boundary, compose the existing
   `squad.Configuration.ProjectRoot.ResolveViaGit` and `ResolveProjectRoot` operations to retain main-checkout and
   linked-worktree discovery without duplicating Git execution in the runtime module. Preserve argument validation
   order and the current project-discovery diagnostic.
5. Preserve `.blaxquad/host.lock`, `.blaxquad/host.json` and its schema, the `blaxquad-*` pipe-name algorithm,
   protocol version `1`, command strings, normalization rules, stale-state locking, readiness values, early-shutdown
   race, server-failure observation, and cleanup order byte-for-byte or behavior-for-behavior as applicable.
6. Update source identifiers, CLI diagnostics, XML comments, Gherkin prose and bindings, test-support names, and
   `docs/Manual/architecture.md`, `modules.md`, and `test-strategy.md` to use Headquarters terminology where "host"
   means the running process. Do not rename persisted artifacts, wire values, provider `IAgentRuntime` concepts,
   generic window-hosting terms, or the `squad.Hosting.*` modules.
7. Extend existing black-box coverage only if the move exposes an observable gap. Do not add tests for namespaces,
   project references, private helpers, or the mere absence of removed types. Keep the focused ownership,
   coexistence, readiness, lifecycle, early-shutdown, startup-failure, termination, and cleanup scenarios green,
   then run the full backend Gherkin suite.
8. From cleaned outputs, build the solution and publish `squad-hq`. Confirm the dependency manifest and publish
   directory contain `squad.Runtime.dll` exactly once and contain neither old assembly. Search tracked source,
   project/solution files, documentation, scripts, and generated-spec inputs for obsolete assembly, namespace, and
   process-control terminology; do not edit `bin` or `obj` output as source.

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
  early shutdown, readiness polling, control-server failure, normal shutdown, and cleanup behavior remain covered
  through the real `squad-hq` process.
- The manuals describe the resulting module and types without preserving obsolete compatibility wrappers.
- The full backend Gherkin suite and product build pass. No friend assembly, reflection access, direct product-object
  test, or test-only production branch is introduced.

## Non-goals

- Combining named-pipe, lock, or metadata logic directly into `SquadApplication`.
- Changing the control wire protocol or persisted host-state filenames.
- Moving `shutdown` or `wait-for-agent` command presentation out of `squad-hq`.
- Redesigning session admission, startup planning, or handoff construction owned by issue 025.
- Renaming `squad.Hosting.Abstractions` or the runtime-loaded hosting implementations from issue 026.
- Creating a reusable control package for hypothetical consumers that do not exist.
