---
title: 500 custom agents
priority: 500
---
# Analyze and Fix Custom Agent Invocation Failure

We have a Copilot SDK-based multi-agent harness.

One of our agents tried to invoke the `Comment Cleanup` custom agent and failed repeatedly with:

```text
Standalone server does not support session effect 'custom_agent_prompt'
```

The agent then correctly escalated instead of bypassing the custom agent:

```text
The `Comment Cleanup` custom agent tool fails in this environment
("Standalone server does not support session effect 'custom_agent_prompt'")
on repeated attempts. Per my role rules, this is a tool-broken condition I
should escalate to the Architect via the handoff path rather than route around
myself.
```

## Goal

Analyze the root cause of this failure in our codebase and fix our Copilot SDK integration so that custom agents/sub-agents such as `Comment Cleanup` can be invoked reliably.

Do not work around the issue by having the caller perform the `Comment Cleanup` task itself. We explicitly want proper agent delegation and role separation.

## Suspected Cause

One likely explanation is that our harness exposes repository-defined custom agents using a mechanism intended for Copilot CLI sessions.

That mechanism may attempt to mutate the current session using the internal session effect:

```text
custom_agent_prompt
```

The standalone server used by the Copilot SDK apparently does not support that effect.

The SDK may instead require custom agents to be registered through the SDK-native custom-agent/sub-agent configuration, e.g. conceptually through something like:

```csharp
SessionConfig.CustomAgents
```

with each custom agent having its own:

* name
* description
* prompt
* allowed tools
* model/configuration as appropriate

Do not assume this hypothesis is correct. Verify it against the actual SDK API/version used by this repository and against our implementation.

## Analysis

Inspect the codebase and determine:

1. How `.github/agents/**` or other custom-agent definitions are currently discovered.

2. How they are exposed to the running Copilot session.

3. What happens internally when an agent invokes another custom agent.

4. Where `custom_agent_prompt` originates.

5. Whether we are accidentally relying on Copilot CLI-specific behavior that is unsupported by the standalone SDK server.

6. What the Copilot SDK version currently used by the project officially supports for:

   * custom agents
   * sub-agents
   * agent delegation
   * isolated prompts/context
   * tool restrictions
   * model selection
   * sub-agent lifecycle/events

7. Whether upgrading the Copilot SDK is required.

8. Whether there are relevant SDK issues, breaking changes, or preview-version limitations that explain the error.

## Desired Architecture

Prefer an SDK-native implementation.

Repository-defined agents should still remain the source of truth where practical, for example:

```text
.github/agents/
    architect.agent.md
    implementer.agent.md
    verifier.agent.md
    comment-cleanup.agent.md
```

Our harness may parse these definitions and translate them into the SDK's native custom-agent configuration.

Conceptually:

```text
.agent.md
   │
   ▼
BlaXquad agent loader
   │
   ▼
SDK CustomAgentConfig
   │
   ▼
SessionConfig.CustomAgents
   │
   ▼
Copilot SDK sub-agent invocation
```

Avoid implementing our own fake sub-agent mechanism if the SDK already provides the required functionality.

## Important Behavioral Requirements

Preserve the following behavior:

* Agents must be able to invoke explicitly available custom agents.
* The delegated agent must receive its own system/custom-agent prompt.
* Role boundaries must remain enforced.
* The parent agent must not silently perform the delegated role itself when delegation fails.
* Tool restrictions defined for an agent must remain enforceable.
* Existing `.github/agents/**` definitions should continue to work if feasible.
* Existing agent discovery should remain compatible unless there is a strong reason to change it.
* Failures during sub-agent execution must be surfaced clearly.
* Cancellation must propagate correctly where supported.
* Sub-agent lifecycle events should be handled if the SDK exposes them.
* Do not introduce CLI/tmux dependencies; this is the Copilot SDK implementation.

## Compatibility

Check whether the change affects:

* existing SDK sessions
* our tmux/CLI implementation, if it shares abstractions
* agent discovery
* tool registration
* handoff logic
* cancellation
* event handling
* transcript rendering
* model configuration

Keep SDK-specific behavior behind the existing abstraction where possible.

## Verification

Add appropriate automated coverage.

At minimum verify:

1. A normal SDK agent can invoke `Comment Cleanup`.

2. `Comment Cleanup` receives its own prompt rather than the caller's role prompt.

3. The sub-agent can execute its allowed tools.

4. The result is returned to the caller exactly once.

5. A failed sub-agent invocation is surfaced as a failure and does not cause the caller to impersonate the sub-agent.

6. Existing non-custom-agent tool calls continue to work.

7. Existing agent discovery still works.

If the SDK exposes lifecycle events such as sub-agent started/completed/failed events, verify that our event processing handles them correctly.

## Root Cause First

Do not start by patching the error message or suppressing the exception.

Trace the invocation path and identify exactly why:

```text
custom_agent_prompt
```

is being sent to a standalone server that does not support it.

I want the underlying integration mismatch fixed.

## Deliverable

First perform the analysis.

If the proposed solution is feasible, implement the fix and tests.

Document:

* root cause
* old invocation path
* new invocation path
* SDK APIs used
* any SDK/version upgrade required
* compatibility implications
* tests added

If there is a meaningful architectural decision or the change is larger than a straightforward SDK integration fix, write an implementation plan under:

```text
docs/issues/
```

before making invasive changes.

Prefer the smallest clean fix that uses the Copilot SDK as intended rather than reproducing Copilot CLI internals in BlaXquad.
