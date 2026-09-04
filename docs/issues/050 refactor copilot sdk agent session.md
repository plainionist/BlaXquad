---
title: Extract Copilot SDK interaction coordination
priority: 50
---

# Extract Copilot SDK interaction coordination

`CopilotSdkAgentSession` is a reasonable SDK session adapter, but it also implements a separate request/response broker
for permissions, inputs, and elicitations. That broker has its own synchronized state and lifecycle independent of SDK
message sending and telemetry refresh.

## Refactoring direction

Extract one SDK-specific pending-interaction coordinator that owns:

- request ID creation;
- pending permission, input, and elicitation completions;
- request publication and response correlation;
- cancellation and failure completion; and
- duplicate or unknown response validation.

Keep `CopilotSdkAgentSession` responsible for adapting `IAgentSession` to `CopilotSdkRuntimeSession`, publishing the
agent event stream, refreshing telemetry, and coordinating session failure/disposal. The extracted coordinator should
use generic private mechanics internally but retain typed request/response APIs at its boundary.

Do not extract thin wrappers for every request kind, and do not move runtime ownership away from
`CopilotSdkAgentSession`.

## Acceptance criteria

- Pending interaction state and correlation no longer live in `CopilotSdkAgentSession`.
- Permission, input, and elicitation requests are published once and resolve only their matching response.
- Cancellation, backend failure, and disposal complete all pending requests with the same observable outcome as today.
- Harness-message echo suppression, event ordering, telemetry refresh, and runtime teardown behavior remain unchanged.
- Existing black-box Gherkin interaction and SDK session scenarios remain green.

## Why priority 50

The extraction has a clear boundary and lowers complexity in the SDK adapter, but its benefit is localized compared with
the core orchestration and lifecycle refactors.