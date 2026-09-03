---
title: 510 sub agents
priority: 510
---
We need to improve how Copilot subagents are represented in the transcript.

## Current problem

Subagent activity currently leaks low-level SDK/runtime details into the transcript, for example:

```text
> read_agent {"agent_id":"e96ea668-b7bf-48dd-a425-be04a0ffbb31","wait":true}
```

This is not useful to the user.

The transcript should instead show meaningful semantic information about the subagent, ideally including:

* what kind of agent it is: Explore, Code Review, Research, Rubber Duck, etc.
* the model being used
* when available, the task-specific name or description

For example:

```text
>> Code Review · gpt-5.6-sol · Review authentication changes
```

or:

```text
>> Explore · claude-sonnet-5 · Find authentication entry points
```

The `>> ` prefix should use the existing green transcript styling.

## Important: investigate the Copilot SDK events first

Do not build this by parsing prompts or guessing from `agent_id`.

The Copilot SDK exposes dedicated subagent lifecycle events.

In particular investigate the SDK types/events currently used by this project for:

```text
subagent.selected
subagent.started
subagent.completed
subagent.failed
```

According to the SDK, `subagent.started` can expose information such as:

```text
toolCallId
agentName
agentDisplayName
agentDescription
model
```

Also inspect the corresponding `task` tool invocation.

Copilot CLI task invocations can contain additional task-specific metadata conceptually like:

```text
agent_type
name
description
model
prompt
```

Verify the exact fields against the SDK/runtime version used by this repository rather than assuming them.

## Desired information priority

For transcript presentation, prefer semantic information in roughly this order:

1. task-specific name or description, if useful
2. `agentDisplayName`
3. `agentName`
4. model

Do NOT derive classifications such as "review", "research", or "search" by inspecting prompt text if Copilot already provides the agent type/name.

Examples of built-in Copilot agents include:

```text
explore
task
general-purpose
code-review
research
rubber-duck
security-review
```

Use Copilot-provided display names where possible rather than maintaining our own duplicated mapping.

## Correlation

Investigate how the following relate:

```text
task tool.execution_start
        ↓
subagent.started
        ↓
subagent events / tool executions
        ↓
read_agent
        ↓
subagent.completed / failed
```

In particular, determine whether `toolCallId` gives us enough correlation to associate the semantic subagent metadata with subsequent transcript activity.

There may also be an `agentId` on the SDK event envelope depending on the SDK version. Use it if reliably available, but do not depend on undocumented behavior.

The goal is to avoid using the opaque UUID as presentation information.

## Transcript behavior

The normal transcript should no longer render subagent plumbing such as:

```text
> task {...}
> read_agent {"agent_id":"..."}
> list_agents {...}
```

when we can represent the same activity semantically.

Instead render something along the lines of:

```text
>> Code Review · gpt-5.6-sol
```

and, when useful task-specific information exists:

```text
>> Code Review · gpt-5.6-sol · Review authentication changes
```

Possible examples:

```text
>> Explore · claude-sonnet-5 · Find authentication entry points

>> Code Review · gpt-5.6-sol · Review current changes

>> Rubber Duck · claude-opus-4.8 · Critique implementation plan

>> Research · gpt-5.6-sol · Investigate Copilot SDK cancellation semantics
```

Exact punctuation/layout should follow existing transcript conventions and remain compact.

## Important distinction

`agentDescription` may describe the general purpose of the agent rather than the specific task it is currently performing.

If `task.name` or `task.description` contains task-specific information, prefer that for the optional final portion of the transcript entry.

For example, prefer:

```text
>> Code Review · gpt-5.6-sol · Review authentication changes
```

over displaying a long generic description of what the Code Review agent does.

## Scope

This is a presentation/transcript change only.

Do NOT change:

* subagent orchestration
* agent selection
* model selection
* execution behavior
* waiting behavior
* SDK calls
* agent lifecycle
* handoff behavior

Opaque IDs may continue to be used internally and in diagnostic/raw logs.

## Fallbacks

Degrade gracefully based on available information.

Ideal:

```text
>> Code Review · gpt-5.6-sol · Review authentication changes
```

Without task description:

```text
>> Code Review · gpt-5.6-sol
```

Without model:

```text
>> Code Review
```

With almost no metadata:

```text
>> Subagent
```

Never expose the raw UUID merely because richer metadata is temporarily unavailable.

## Implementation guidance

Keep the change small and localized to transcript presentation.

Prefer consuming the existing semantic `subagent.*` SDK events over building our own interpretation of low-level `read_agent` calls.

If some correlation state is necessary, keep it as lightweight presentation state and do not let orchestration depend on it.

Add focused tests for the transcript formatting.

First analyze the SDK event flow and existing transcript implementation, then implement the smallest clean solution.
