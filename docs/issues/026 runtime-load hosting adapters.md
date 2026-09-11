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

Issue 025 has already been completed and removed from the active issue catalog. Do not restore it. This issue
supersedes its historical instruction to inline and retain the static `UiMode` branch:
`IWindowHost`/`ISleepInhibitor` remain because the runtime-loaded hosting SPI gives those contracts real process-time
variation.

## Implementation plan

The current manual conflict is intentional scope, not an alternative design:
`docs/Manual/test-strategy.md` still calls `--ui stdio` a supported product transport. Slice 1 corrects that statement
when the backend harness moves to explicit hosting selection. The slices below are ordered dependencies; hand off
and accept exactly one before starting the next.

### Slice 1: Runtime-load the test-owned stdio host

**Outcome:** Every backend acceptance launch selects the stdio hosting bundle with an explicit `--hosting`
descriptor, while `squad-hq` no longer references stdio or exposes the built-in `--ui` mode.

1. Add `IHostingFactory`, `HostingContext`, and `HostingRuntime` to `squad.Hosting.Abstractions`, including the
   required `squad.Ui.Abstractions` dependency. Keep `SquadApplication` as the owner of the returned
   `IWindowHost` and `ISleepInhibitor`, preserving startup order and failure-safe cleanup.
2. Add hosting descriptor parsing and loading for `--hosting <assemblyPath>;<factoryType>`. Resolve relative paths
   against the launcher's current directory and report controlled, hosting-specific diagnostics for duplicate
   options, missing values, malformed descriptors, missing or unloadable assemblies, non-public or incompatible
   types, missing public parameterless constructors, and constructor failures.
3. Extract only the assembly-loading mechanics genuinely shared with provider loading. Keep provider and hosting
   entry points, messages, and non-collectible load contexts separate. The hosting context must unify
   `squad.Hosting.Abstractions`, `squad.Ui.Abstractions`, and every contract assembly exposed through those APIs,
   while resolving private managed and native dependencies beside the plug-in.
4. Rename `squad.Stdio` to `squad.Hosting.Stdio`; add the sole public `StdioHostingFactory`, make the window host
   internal, and return it with an internal no-op sleep inhibitor. Keep the headless client, wire decoding, waits,
   reconciliation, and diagnostics in `squad.Specs`.
5. Route an explicit hosting descriptor through `Launch`, but preserve the current directly composed Photino
   default only until Slice 2. Remove `UiOption`, `UiMode`, the `--ui` branch, and the `squad.Hosting.Stdio`
   project reference from `squad-hq`.
6. Centralize construction of the provider and hosting descriptors in `BackendScenario` and use both on every
   launch path, including continue, caller cancellation, pre-readiness, missing-helper, and replacement launches.
7. Replace `UiSelection.feature` with black-box hosting-selection scenarios using public plug-in fixtures. Include
   a valid load and every diagnostic above, plus a fixture deployed with duplicate contract assemblies so the test
   fails if contract types are loaded into the plug-in context instead of unified with headquarters.
8. Update stdio feature text, test-support comments, module documentation, and the backend test strategy to call
   stdio a test-distributed hosting adapter selected by the harness, never a supported product presentation mode.

**Acceptance:** The focused hosting-selection and provider-selection scenarios pass through the published process;
the stdio protocol, readiness, concurrent command admission, EOF, cancellation, shutdown, startup-failure, and
cleanup scenarios run through the explicit stdio factory; provider diagnostics are unchanged; and source/dependency
searches show no `--ui`, `UiMode`, `UiOption`, or `squad.Hosting.Stdio` reference in `squad-hq`.

**Status: changes requested (f861f8d244)**

#### Review findings on f861f8d244

**Finding 1 — medium**

- **Location:** `src/squad.Specs/Features/HostingSelection.feature`
  (`A hosting plug-in deployed alongside duplicate contract assemblies still loads`) and
  `ScenarioWorkspace.CreateHostingFixtureDeploymentWithDuplicateContracts`.
- **Violated behavior:** Slice 1 requires a fixture deployed with duplicate contract assemblies so the scenario
  fails if contract types are loaded into the plug-in context instead of unified with headquarters.
- **Root cause:** The isolated deployment copies the plug-in and contract DLLs but not the plug-in's
  `.deps.json`. `PluginLoadContext` resolves sibling assemblies only through `AssemblyDependencyResolver`, which
  needs that manifest. Without it, `Load` returns null and the default context still supplies headquarters'
  contract assemblies, so omitting names from `HostingLoader`'s shared set would still complete the ready handshake.
- **Required outcome:** Deploy the plug-in in a layout `AssemblyDependencyResolver` actually uses, including
  dependency metadata that maps the copied contract DLLs. With unification disabled, the published process must
  fail the ready handshake with a type-incompatible hosting diagnostic; with unification enabled, it must still
  complete the handshake.

**Finding 2 — medium**

- **Location:** `src/squad.Specs/Features/HostingSelection.feature`
- **Violated behavior:** Slice 1 requires a black-box hosting diagnostic for unloadable assemblies, distinct from
  a missing file, as part of "every diagnostic" the hosting-selection scenarios must cover.
- **Root cause:** `PluginLoader` maps `BadImageFormatException`, `IOException`, and `UnauthorizedAccessException`
  to "Hosting assembly could not be loaded", but no scenario launches `--hosting` against a present, unloadable
  file. Hosting-selection covers assembly-not-found, not unloadable.
- **Required outcome:** Launch the published process with `--hosting` pointing at an existing non-assembly file
  and prove a non-zero exit, stderr containing the unloadable diagnostic, and no unhandled exception.

**Status: changes applied, ready for re-review**

#### Response to review findings

**Finding 1 — resolved.** Added a `PublishHostingStdioFixture` MSBuild target (`squad.Specs.csproj`) that
`dotnet publish`es `squad.Hosting.Stdio` standalone (framework-dependent, matching the existing
`squad.AgentProvider.CopilotSdk` packaging target's pattern) into its own directory before every test run,
producing a real `squad.Hosting.Stdio.deps.json` alongside its own copies of `squad.Hosting.Abstractions`,
`squad.Ui.Abstractions`, `squad.AgentProvider.Abstractions`, and `squad.Ui.Protocol`. `ScenarioWorkspace` now
points the duplicate-contract-assembly scenario at that published output instead of an ad hoc copy of select DLLs.
Manually verified causality: with `HostingLoader.SharedAssemblyNames` temporarily emptied, the scenario fails with
`Hosting type 'squad.Hosting.Stdio.StdioHostingFactory' was not found in '...', or does not publicly implement
IHostingFactory.` (a real type-incompatible diagnostic, since `AssemblyDependencyResolver` now genuinely resolves
the local duplicate copies via the deps.json); restoring the shared-name set makes it complete the ready handshake
again, exactly as required.

**Finding 2 — resolved.** Added scenario "An unloadable hosting assembly fails clearly" to
`HostingSelection.feature` plus its step, which points `--hosting` at a real, existing, non-assembly file
(triggering `BadImageFormatException`) and asserts a non-zero exit, `"could not be loaded"` in stderr, and no
unhandled exception - reusing the existing "the launch fails with a hosting diagnostic containing" assertion step.

All 169 `squad.Specs` tests pass, including both new/changed scenarios.

### Slice 2: Runtime-load and package Photino as the default

**Outcome:** Omitting `--hosting` loads a complete Photino hosting bundle from the packaged sibling plug-in, with no
concrete hosting reference or construction in `squad-hq`.

1. Rename `squad.Photino` to `squad.Hosting.Photino`, align namespaces and solution entries, add the sole public
   `PhotinoHostingFactory`, and make `PhotinoWindowHost`, `SleepInhibitor`, and other implementation details
   internal.
2. Replace the temporary direct default in `Launch` with a descriptor containing only the default sibling assembly
   and factory type names. Create both lifecycle resources from one `HostingContext`; do not change
   `SquadApplication` startup, cancellation, or aggregate-disposal behavior.
3. Replace the asset-only target with build and publish packaging for the standalone Photino component. Copy its
   assembly, component dependency metadata, private managed dependencies, runtime-native assets, Vue distribution,
   and window assets, while excluding every contract assembly already supplied by headquarters.
4. Add `IncludePhotinoHosting`, defaulting to `true`, and make ordinary build/run plus normal publish resolve the
   sibling plug-in. Move the executable icon to executable-owned assets so packaging does not recreate a project
   dependency.
5. Add black-box packaging/selection scenarios proving that `squad-hq` has no compile-time dependency on either
   concrete hosting assembly, the normal output includes Photino and excludes stdio, omitted selection resolves the
   default factory, and an explicit Photino descriptor selects the same plug-in without changing the executable.
6. Update architecture, module inventory, glossary, UI documentation, and CLI help/comments with the process-time
   hosting boundary and Photino's status as the packaged default.

**Acceptance:** Both normal build output and publish output can launch through the default factory; the publish
contains the Photino component metadata, managed/native dependencies, and UI assets but no stdio plug-in;
`squad-hq.csproj`, its dependency manifest, and `Launch` contain no concrete hosting dependency; and existing
provider packaging and lifecycle scenarios remain unchanged.

### Slice 3: Publish a genuinely headless backend test package

**Outcome:** The backend suite runs the same provider-neutral and hosting-neutral `squad-hq` executable from a
publication containing neither default product plug-in nor visual UI payload.

1. Publish the backend-spec tools with both `IncludeCopilotSdkProvider=false` and
   `IncludePhotinoHosting=false`. Build `squad.Hosting.Stdio` as a test fixture dependency of `squad.Specs` and load
   it by the absolute descriptor supplied from the runner's own output, never by copying it into the
   `squad-hq` publication.
2. Extend the packaging feature to assert that the backend-spec directory contains the executable and shared
   contracts but no Copilot provider, Photino hosting assembly, `Photino.NET`, `Photino.Native`, Photino runtime
   directory, Vue distribution, or Photino window assets. Check the executable dependency manifest as well as
   physical files.
3. Prove the independently distributed stdio plug-in still loads private dependencies and unifies the host's
   contracts, then run the complete backend Gherkin suite against that headless publication. Keep the normal
   product publication assertions in the same feature so the opt-out cannot weaken default packaging.
4. Finish the test-strategy documentation for the two-package arrangement and remove stale assembly names,
   publish properties, generated-spec inputs, scripts, and comments as part of this packaging outcome. Do not
   restore completed issue 025.

**Acceptance:** The packaging scenarios prove physical and manifest-level absence of both default plug-ins and all
Photino/UI payload from the backend-spec publication; the full backend suite drives the real process through the
separately built stdio plug-in; the frontend Playwright suite and product build still pass; and repository-wide
searches find only the documented new hosting names and supported `--hosting` option.

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
