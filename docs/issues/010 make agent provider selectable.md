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
