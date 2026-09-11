---
title: modularization continued
priority: 3
---

## Goal

Make both agent-provider adapters explicit modules:

- rename the production Copilot adapter from `squad.CopilotSdk` to
  `squad.AgentProvider.CopilotSdk`; and
- extract the acceptance-suite fake adapter from `squad.Specs` into
  `squad.AgentProvider.Fake`.

The provider-neutral contracts and runtime behavior must remain unchanged.
Documentation changes travel with the module change they describe rather than
forming a documentation-only slice.

## Slice plan

Only one slice may be implemented or reviewed at a time. Slice 2 starts only
after Slice 1 is accepted.

### Slice 1 - Rename the production Copilot provider module [done]

**Outcome:** The default provider is built, packaged, discovered, and
documented exclusively as `squad.AgentProvider.CopilotSdk`, while
`squad-hq` remains provider-neutral at compile time.

Implementation:

1. Move `src\squad.CopilotSdk` to
   `src\squad.AgentProvider.CopilotSdk`, rename its project and assembly, and
   change every provider namespace to `squad.AgentProvider.CopilotSdk`.
2. Update `squad.slnx`, the `squad-hq` publish-target import, the standalone
   provider publish inputs, dependency-manifest filenames, and the default
   provider assembly/type descriptor. Preserve the existing
   `IncludeCopilotSdkProvider` opt-out behavior and the shared
   `squad.AgentProvider.Abstractions` load-context boundary.
3. Update the provider-packaging Gherkin and bindings to use the new assembly
   name. The default publish must contain the renamed provider and Copilot
   runtime assets; provider-free and opted-out publishes must contain neither
   the renamed provider nor stale `squad.CopilotSdk` artifacts.
4. Replace the old module name in active source comments and in
   `docs\manual\modules.md` and `docs\manual\test-strategy.md`. Do not edit
   generated `*.feature.cs` files.

Acceptance criteria:

- `squad.AgentProvider.CopilotSdk.dll` contains
  `squad.AgentProvider.CopilotSdk.CopilotSdkAgentProviderFactory` and is the
  assembly selected by the default launch descriptor.
- A normal `squad-hq` publish packages that assembly, its managed SDK
  dependency, and its native runtime assets without adding a compile-time
  provider dependency to `squad-hq`.
- Opted-out and backend-spec publishes omit all Copilot-provider assemblies,
  dependencies, and runtime assets.
- The focused `ProviderPackaging.feature` scenarios pass, the solution builds,
  and active source and manual documentation contain no old module or
  namespace reference.

**Status: complete (c725eeb42c).** The default provider project, assembly,
namespaces, publish-target import, dependency-manifest filename, and launch
descriptor are `squad.AgentProvider.CopilotSdk`. `squad-hq` still has no
compile-time provider project reference; packaging copies the renamed
assembly, `GitHub.Copilot.SDK.dll`, and native runtime assets unless
`IncludeCopilotSdkProvider` is false. `ProviderPackaging.feature` and bindings
assert the new assembly name; active source and `docs/manual/modules.md` /
`docs/manual/test-strategy.md` no longer use `squad.CopilotSdk`.

Slice 1 is complete. Slice 2 remains pending until the architect activates it.

### Slice 2 - Extract the fake provider module

**Outcome:** Backend acceptance scenarios load their fake provider from the
standalone `squad.AgentProvider.Fake` adapter assembly, while `squad.Specs`
retains only scenario orchestration and bindings.

Implementation:

1. Add `src\squad.AgentProvider.Fake\squad.AgentProvider.Fake.csproj`, reference
   only `squad.AgentProvider.Abstractions`, include it in `squad.slnx`, and move
   all files currently under `src\squad.Specs\Support\Agents` into that module.
   Rename their namespaces to `squad.AgentProvider.Fake` and
   `squad.AgentProvider.Fake.Control`.
2. Reference the new project from `squad.Specs` and adapt all scenario support,
   bindings, type descriptors, comments, and XML references. Provider
   descriptors must use the fixture type's assembly location, not
   `squad.Specs.dll`, so the real provider loader opens the standalone adapter.
3. Keep public only the reflection-loaded provider entry types required by the
   production loader: `FakeAgentProviderFactory`,
   `ValidProviderFixtureFactory`, `ThrowingProviderFixtureFactory`, and
   `IncompatibleProviderFixture`. Make the fake runtime, sessions, event
   stream, control transport, and control server internal; grant
   `squad.Specs` access with `InternalsVisibleTo` and narrow any Specs helper
   members that would otherwise expose those internal types.
4. Add a focused Gherkin assertion for the assembly boundary, retain the
   provider-selection diagnostics for the moved fixture types, and exercise
   one complete fake-provider process lifecycle through the real
   `--provider` SPI and control pipe.
5. Add `squad.AgentProvider.Fake` to `docs\manual\modules.md` and update
   `docs\manual\test-strategy.md` so it describes loading the fake provider
   from its dedicated assembly rather than from `squad.Specs.dll`.

Acceptance criteria:

- `FakeAgentProviderFactory` is defined by
  `squad.AgentProvider.Fake.dll`, not `squad.Specs.dll`, and a provider-free
  published `squad-hq` loads it by explicit descriptor.
- The four loader-facing fixture types are the fake module's only public
  top-level types; its implementation and control surface are internal.
- The focused `AgentProviderSelection.feature`, `ProviderPackaging.feature`,
  `HeadquartersLifecycle.feature`, and `StdioUiProtocol.feature` scenarios
  covering provider loading, lifecycle, control transport, prompt/reply, and
  clean shutdown pass.
- The solution builds with no source remaining under
  `src\squad.Specs\Support\Agents`, and the manual module inventory and test
  boundary describe both provider assemblies accurately.
