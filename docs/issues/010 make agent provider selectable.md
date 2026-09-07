---
title: Make the agent provider selectable
priority: 10
---

# Make the agent provider selectable

This issue implements the [backend test strategy](../manual/test-strategy.md).

## Goal

Allow the actual `squad-hq` executable to run with a provider-neutral agent implementation and without loading
`squad.CopilotSdk`.

Replace the current hardcoded construction of `CopilotSdkRuntimeModeFactory` with one explicit, trusted provider
selection mechanism. The provider contract must be cohesive and receive a prepared `AgentBackendContext` value rather
than deferred context delegates.

The production package must continue to select the Copilot SDK provider by default. Tests will later select a fake
provider implemented inside `squad.Specs`.

Do not add a fake-provider branch, test mode, or test callback to product code. Do not discover provider code from the
target workspace.

## Implementation plan

### Slice 1: Establish the prepared-context provider contract

**Status: complete (46928a39fa)**

1. Replace `IRuntimeModeFactory` with a provider-focused `IAgentProviderFactory` in
   `squad.AgentProvider.Abstractions`. Keep only the provider name and one asynchronous creation operation accepting a
   non-null, fully prepared `AgentBackendContext` value plus `CancellationToken`; remove `IsAvailable`, the separate
   preparation method, and every deferred context delegate.
2. Rename the Copilot implementation to match the new contract and make `CopilotSdkBackend` retain the prepared
   context value. Runtime generations continue to create fresh Copilot clients and sessions from that same immutable
   launch context, and the backend remains the owner of fatal provider failure reporting.
3. Make `SquadStartupPlan` return the prepared `AgentBackendContext` from its existing context-preparation phase.
   `SquadApplication` accepts the already selected `IAgentProviderFactory`, awaits backend creation immediately after
   that phase, and only then creates/starts the runtime controller. This keeps provider construction inside the
   existing startup cancellation, host-control shutdown, failure observation, and cleanup lifecycle.
4. Change `RuntimeMode` to carry the selected factory instead of a prematurely created backend and preparation
   delegate. Headquarters remains responsible for building the concrete context and selecting/wiring the factory;
   host runtime only invokes that provider-neutral factory at the established lifecycle boundary.
5. Keep `SquadRuntimeController`, `SessionGeneration`, and the provider-neutral
   `IAgentBackend`/`IAgentRuntime`/`IAgentSession`/typed-event contracts unchanged. Do not introduce a lazy backend,
   mutable context holder, provider callback, or pass-through factory.
6. Preserve partial-startup cleanup: a backend returned by the factory becomes owned by `SquadApplication` before any
   later startup operation, while a factory that fails before returning remains responsible for resources it did not
   transfer. Shutdown and cancellation during provider creation must retain their current bounded startup semantics.
7. Update existing acceptance support and run the focused startup, cancellation, provider-failure, session-relaunch,
   and shutdown scenarios, followed by the complete build and acceptance suite. Do not add a scenario solely for the
   renamed internal contract.

**Slice acceptance**

- The sole process-time provider contract creates an `IAgentBackend` asynchronously from one prepared
  `AgentBackendContext` value.
- No provider-selection API accepts `Func<AgentBackendContext>`, optional test collaborators, or a separate
  prepare/create lifecycle.
- Copilot-backed launches preserve role definitions, environment construction, runtime generations, failure
  propagation, and cleanup behavior.
- Provider selection and concrete context construction remain in `squad-hq`; provider/runtime lifecycle remains in
  `SquadApplication` and the existing backend/runtime owners.

### Slice 2: Load an explicitly selected provider and package the default

**Status: complete (af51a88eac)**

1. Add a small headquarters command-line provider selection value and parser. `squad-hq launch` accepts at most one
   `--provider` descriptor containing an assembly path and public factory type name; it remains compatible with
   `--continue` and the optional workspace path. Resolve an explicit relative assembly path against the launcher's
   current directory, never against the target workspace or its configuration.
2. When `--provider` is omitted, construct the same descriptor from the installed application base directory and the
   Copilot provider's known assembly/type names. Keep these as data strings so `squad-hq` has no source or assembly
   reference to `squad.CopilotSdk`; the default must not inspect the workspace or search arbitrary directories.
3. Add one provider loader responsible for loading exactly the selected assembly, resolving its private dependencies
   beside that assembly while sharing `squad.AgentProvider.Abstractions` with the host, validating that the named type
   is public, concrete, and implements `IAgentProviderFactory`, and invoking its public parameterless constructor.
   Keep the load context alive for the selected provider's process lifetime.
4. Convert provider selection failures into concise `CliExitException` diagnostics that identify the selected
   descriptor and distinguish a missing/unreadable assembly, missing or incompatible type, repeated `--provider`
   selection, and constructor failure. Preserve the original exception as the cause where applicable, but do not emit
   an unhandled exception trace for expected selection errors.
5. Remove the Copilot namespace use and compile-time project reference from `squad-hq`. Extend the existing
   Copilot publish target so a normal headquarters publish builds and copies the provider assembly, its managed
   dependencies, and native runtime assets as packaging inputs only; a headquarters build/publication that disables
   that packaging input must still compile without loading or containing `squad.CopilotSdk`.
6. Add black-box Gherkin scenarios in `squad.Specs` that launch the real published executable and cover the explicit
   provider descriptor plus each failure category. Put minimal public fixture factory types in `squad.Specs` itself so
   exact type selection can exercise valid, incompatible, and throwing construction without a new test assembly,
   product test branch, `InternalsVisibleTo`, or reflection over product internals.
7. Extend the existing architecture/package assertions to prove that `squad-hq` has no compile-time Copilot
   dependency, an opt-out publish omits the Copilot assembly, and the normal production publish contains the default
   provider and runtime assets. Run the focused provider-selection/startup scenarios, the existing Copilot-backed
   launch coverage, `dotnet publish` in both modes, and the complete build and acceptance suite.

**Slice acceptance**

- Explicit provider selection loads only the command-line assembly/type descriptor and never discovers code from the
  target workspace.
- Missing assemblies, incompatible types, duplicate provider options, and constructor failures return clear nonzero
  process results without unhandled traces.
- `squad-hq` compiles and can be published without `squad.CopilotSdk`, while the normal production package includes
  and selects the Copilot provider without an extra user action.
- Alternate providers need only the public provider-neutral SPI; no test-specific product API or additional test
  assembly is introduced.
- Existing default Copilot launch and provider-neutral runtime behavior remain unchanged.

## Acceptance criteria

- `squad-hq` has no compile-time dependency on `squad.CopilotSdk`.
- The production package can still launch with the Copilot SDK provider without an additional user step.
- An explicitly selected provider factory can be loaded from a trusted assembly outside the target workspace.
- Provider selection fails clearly for a missing assembly, incompatible type, duplicate provider, or construction
  failure.
- The provider factory receives a prepared `AgentBackendContext`; the selection API does not accept
  `Func<AgentBackendContext>` or nullable test collaborators.
- `IAgentBackend`, `IAgentRuntime`, `IAgentSession`, and typed agent events remain the provider-neutral runtime SPI.
- Provider loading does not introduce a new test assembly or expose product internals to `squad.Specs`.
- Existing Copilot-backed launch behavior remains unchanged.
