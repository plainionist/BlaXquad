---
title: Runtime-load headquarters hosting adapters
priority: 4
---

# Runtime-load headquarters hosting adapters

## Problem

`squad-hq` statically references both `squad.Photino` and `squad.Stdio`, parses a built-in
`--ui <photino|stdio>` choice, and constructs the selected `IWindowHost` directly. This makes stdio look like a
supported product presentation mode even though it was introduced only so the backend specifications can drive the
real UI protocol without opening Photino or running Vue.

The acceptance harness needs both sides of that test boundary:

```text
squad.Specs process                         squad-hq process
-------------------                         ----------------
HeadlessUiClient
        |                                     StdioWindowHost
        `--------- JSON over stdio ----------> UiProtocolSession
```

They are used together, but they have different ownership. `StdioWindowHost` is the child-process hosting adapter;
`HeadlessUiClient` is the test runner's independently implemented protocol client and observation facade. Moving the
client into a product assembly would couple the black-box suite to the producer's implementation and ship test
waiting, diagnostics, and reconciliation code.

The current static composition has additional consequences:

- every `squad-hq` build has compile-time dependencies on both concrete hosting implementations;
- the backend-spec publication still carries Photino, `Photino.Native`, and UI assets even when it selects stdio;
- the stdio path still constructs `SleepInhibitor` from the Photino assembly;
- `IWindowHost` and `ISleepInhibitor` abstract runtime lifecycle calls but do not form a process-time hosting SPI;
- the manual describes line-delimited stdio as a supported non-visual product transport, contrary to its intended
  test-only use; and
- issue 025 treats Photino and stdio as two genuine production transports and asks `Launch` to retain a static
  `UiMode` branch. That premise is superseded by this issue.

Provider loading already demonstrates the required boundary: `squad-hq` knows only the provider contract and a
descriptor, the default implementation is a packaging input rather than a compile-time dependency, and backend
specifications explicitly load their fake implementation into the real child process. Hosting should use the same
process-time model.

## Goal

Load one complete hosting implementation at process startup through `squad.Hosting.Abstractions`:

- the normal product package includes and selects Photino by default;
- backend specifications explicitly select a test-distributed stdio implementation;
- `squad-hq` has no compile-time reference to either concrete hosting assembly;
- a backend-spec publication can physically omit Photino, its managed/native dependencies, and its UI assets; and
- no workspace configuration, product test branch, direct product-object test, or in-process test substitution is
  introduced.

Keep the versioned UI protocol and authoritative application state independent of hosting selection. This issue is
about process composition and packaging, not redesigning the UI protocol.

## Required design

### Define one cohesive hosting factory

Add a process-time factory to `squad.Hosting.Abstractions`. Conceptually:

```csharp
public interface IHostingFactory
{
    string Name { get; }

    HostingRuntime Create(HostingContext context);
}

public sealed record HostingContext(
    string WorkingDirectory,
    ISquadUi Ui,
    IIssueCatalog IssueCatalog);

public sealed record HostingRuntime(
    IWindowHost WindowHost,
    ISleepInhibitor SleepInhibitor);
```

The exact value-type names may change, but the contract must create the complete hosting bundle. Loading only an
`IWindowHost` is insufficient because headquarters would still need to know which concrete sleep implementation
belongs to the selected host. Do not add default interface implementations; every hosting plug-in must explicitly
provide both resources.

`SquadApplication` remains the lifecycle owner of the returned window host and sleep inhibitor unless consolidating
their disposal behind one hosting-runtime owner demonstrably simplifies failure-safe cleanup. Startup ordering,
partial-startup cleanup, cancellation, and aggregate cleanup diagnostics must remain observable as today.

It is acceptable and intentional for `squad.Hosting.Abstractions` to reference `squad.Ui.Abstractions` for the
factory context. The concrete adapters already require those UI contracts; placing them in the process-time context
makes that dependency explicit without moving protocol behavior into the hosting abstraction.

### Load hosting through an explicit descriptor

Replace the built-in `--ui photino|stdio` selection with an optional process-time descriptor, for example:

```text
--hosting <assemblyPath>;<factoryType>
```

When omitted, headquarters loads the packaged Photino factory from a default sibling assembly using data-only
assembly and type names, just as it does for the default Copilot provider. An explicit descriptor is resolved
against the launcher's current directory, never against or from the target workspace. Do not scan directories or
load a hosting implementation named in `blaxquad/squad.json`.

Report controlled CLI diagnostics for duplicate options, missing or malformed descriptors, missing/unloadable
assemblies, incompatible or non-public types, missing public parameterless constructors, and constructor failures.
Do not retain `--ui stdio` as a hidden test alias or compatibility branch.

Generalize the existing provider loader only where two real plug-in families now share behavior. A reusable loader
may own descriptor validation, `AssemblyDependencyResolver`, construction, and load-context lifetime while thin
provider/hosting entry points retain domain-specific diagnostics. Provider and hosting implementations should use
separate load contexts.

The hosting load context must unify every contract assembly whose types cross the boundary, including
`squad.Hosting.Abstractions` and `squad.Ui.Abstractions` and their exposed provider-neutral types. It must still load
the plug-in's private managed and native dependencies beside that plug-in. Add a black-box failure case that would
detect a duplicate, type-incompatible contract assembly rather than relying only on inspection.

### Make Photino the default packaged plug-in

Rename `squad.Photino` to `squad.Hosting.Photino` and align namespaces and files with that module name. Add one public
`PhotinoHostingFactory`; keep `PhotinoWindowHost`, `SleepInhibitor`, and other implementation details internal where
assembly-crossing access is no longer needed.

The factory creates the Photino window host and its real sleep inhibitor from one `HostingContext`. `squad-hq` must
remove its concrete project reference and all concrete Photino construction.

Replace the current asset-only publish target with packaging analogous to the default provider:

- build/publish the Photino plug-in as a standalone component for the selected runtime;
- copy its assembly, component dependency metadata, private managed dependencies, and native runtime assets;
- exclude shared contract assemblies already supplied by headquarters;
- copy the Vue distribution and application assets required by the window host; and
- make the normal build/run and publish paths both able to resolve the default sibling plug-in.

Keep the default inclusion controllable through an MSBuild property such as `IncludePhotinoHosting`, defaulting to
`true`. Branding owned by the executable, such as its application icon, should move out of the plug-in if retaining
it there would force a compile-time project dependency.

### Make stdio a test-distributed plug-in

Rename `squad.Stdio` to `squad.Hosting.Stdio` and align its namespace. Add one public `StdioHostingFactory` that
creates `StdioWindowHost` plus an internal no-op `ISleepInhibitor`. No Photino type, assembly, asset, or native
dependency may be required by this plug-in.

The stdio project is a test fixture distributed to the backend suite, not a default `squad-hq` product dependency.
The normal product publish must not include it. `BackendScenario` supplies its assembly/type descriptor explicitly
on every launch, in the same way it supplies the fake provider descriptor.

Keep `HeadlessUiClient`, `HeadlessUiTransport`, transcript reconciliation, protocol observations, waits, and process
diagnostics in `squad.Specs`. They consume the serialized wire contract from the other process and must not reuse
the producer's internal protocol objects or `squad.Ui.Abstractions` DTOs merely because the stdio host and client are
paired in tests.

### Produce a genuinely headless backend-spec package

Publish the backend-spec copy of `squad-hq` with both the default provider and default Photino hosting disabled. It
must contain the provider-neutral and hosting-neutral executable but no:

- `squad.AgentProvider.CopilotSdk` implementation;
- `squad.Hosting.Photino` implementation;
- `Photino.NET` or `Photino.Native` binaries;
- Photino runtime-native directories; or
- Vue/Photino UI distribution copied for the default host.

The test runner may load `squad.AgentProvider.Fake` and `squad.Hosting.Stdio` by explicit absolute descriptors from
their own build outputs. The separately published `squad-hq` executable itself must remain the system under test;
do not compile a test-specific executable variant.

### Correct the documented product boundary

Update the architecture, module inventory, test strategy, CLI help/comments, and relevant features:

- Photino is the default product hosting plug-in.
- Hosting is replaceable only through the explicit trusted process-launch option.
- Stdio is the test adapter used by the backend acceptance harness, not a built-in product UI mode.
- The UI protocol remains transport-neutral and exercised over the real process boundary.
- `HeadlessUiClient` remains test-owned and the Vue client remains the production presentation client.

Update issue 025 while implementing this change: remove its instruction to inline and retain the static `UiMode`
branch, and retain `IWindowHost`/`ISleepInhibitor` because the runtime-loaded hosting SPI gives those contracts real
process-time variation.

## Implementation plan

### Slice 1: Establish and characterize hosting selection

1. Recast the current UI-selection scenarios around an explicitly selected hosting factory and characterize all
   loader diagnostics through the real `squad-hq` process.
2. Add `IHostingFactory`, its context/result values, and a hosting descriptor without changing the default runtime
   behavior yet.
3. Extract the genuinely shared provider/hosting assembly-loading mechanics while retaining typed provider errors
   and all existing provider-selection behavior.
4. Add valid, incompatible, and throwing hosting fixtures through the public SPI rather than exposing loader
   internals to tests.

### Slice 2: Load and package Photino by default

1. Rename the Photino module and add `PhotinoHostingFactory`.
2. Move concrete Photino and sleep-inhibitor construction out of `Launch`.
3. Remove the concrete Photino project reference from `squad-hq` and package the plug-in, dependencies, native
   assets, and Vue distribution through build targets.
4. Prove that omitting `--hosting` resolves and constructs the packaged default without adding source-level type
   references back to headquarters.

### Slice 3: Load stdio only for backend specifications

1. Rename the stdio module, add its factory and no-op sleep inhibitor, and remove its project reference from
   `squad-hq`.
2. Change `BackendScenario` to pass explicit provider and hosting descriptors on every launch path.
3. Remove `UiMode`, `UiOption`, and all built-in `photino|stdio` branching.
4. Disable default Photino packaging for the backend-spec executable and add an observable packaging scenario that
   proves no Photino assembly, native binary, or UI asset remains.
5. Run the existing readiness, command-concurrency, EOF, cancellation, protocol-validation, transcript recovery,
   shutdown, and cleanup scenarios against the dynamically loaded stdio host.

### Slice 4: Align documentation and public surface

1. Update the architecture, modules, test strategy, and command documentation to distinguish default product
   hosting from test-distributed stdio hosting.
2. Make concrete hosting types internal wherever only their public factory crosses the assembly boundary.
3. Remove stale names, publish properties, output files, and issue-025 assumptions after searching all source,
   generated-spec inputs, scripts, and documentation.

## Acceptance criteria

- `squad-hq.csproj` has no `ProjectReference` to `squad.Hosting.Photino` or `squad.Hosting.Stdio`, and `Launch` names
  no concrete hosting type.
- A normal build and publish include a resolvable default Photino hosting plug-in and no stdio hosting plug-in.
- Omitting `--hosting` launches through `PhotinoHostingFactory`; an explicit valid descriptor selects that factory
  instead without changing the executable.
- The backend-spec `squad-hq` publication contains no Copilot provider, Photino hosting assembly, Photino managed or
  native dependency, or Vue/Photino UI payload.
- Backend scenarios load the stdio factory explicitly and continue to drive the real versioned UI protocol through
  redirected stdin/stdout of the separately launched process.
- Selecting stdio never constructs or loads `SleepInhibitor`, `PhotinoWindowHost`, `Photino.NET`, or
  `Photino.Native`; the stdio plug-in supplies its own no-op sleep resource.
- Hosting descriptor errors are controlled CLI failures for duplicate, missing, malformed, missing-assembly,
  incompatible-type, missing-constructor, and throwing-constructor cases.
- Provider loading and provider-free packaging retain all existing behavior and diagnostics after any shared loader
  extraction.
- UI-ready ordering, concurrent command admission, EOF-as-close, host-control shutdown, caller cancellation,
  startup failure, and failure-safe disposal remain unchanged for the stdio acceptance path.
- `HeadlessUiClient` and all test observation/reconciliation types remain in `squad.Specs` and decode JSON
  independently of production protocol DTOs.
- The manuals no longer advertise stdio as a built-in product UI mode and accurately describe the hosting SPI,
  default Photino package, and test-only stdio adapter.
- The full backend Gherkin suite, frontend Playwright suite, and product build pass without a test-specific branch,
  friend assembly, reflection over internals, or workspace-controlled plug-in loading.

## Non-goals

- Shipping stdio as a supported end-user UI mode.
- Combining the child-process stdio host with the test runner's headless protocol client.
- Moving wire DTOs or JSON decoding into `squad.Ui.Abstractions`.
- Discovering plug-ins from the target repository or configuration.
- Switching hosting implementations during one headquarters run.
- Making plug-in load contexts collectible or supporting hot reload.
