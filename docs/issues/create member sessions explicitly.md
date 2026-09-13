---
title: Create member sessions explicitly
priority: 20
---

## Problem

`IAgentRuntime.StartAsync` receives the complete member roster indirectly through `AgentBackendContext.Roles`,
creates every session, sends every initial instruction, and reports each session back through a callback. This puts
squad-level orchestration inside provider adapters, hides the one-runtime-to-many-sessions relationship, and makes
`StartAsync` misleading because the provider runtime has already been started by `CreateRuntimeAsync`.

## Goal

Give `IAgentRuntime` a narrow operation that creates and returns one `IAgentSession` for one provider-neutral member
context. The squad-level session coordinator must explicitly create, register, observe, and initialize one session
per configured member. Rename `SessionGeneration` to `SquadSessions`; a `SquadMember` remains provider-independent
and does not create its own session.

The provider runtime continues to own its shared connection and every session it creates, including sessions
created before a partial startup failure.

## Acceptance criteria

- `IAgentRuntime` exposes `CreateSessionAsync(AgentMemberContext, CancellationToken)` and no bulk `StartAsync`
  callback API.
- `AgentBackendContext` contains only runtime-wide configuration; it does not contain or iterate the squad roster.
- `SquadSessions` calls `CreateSessionAsync` once per configured member and registers event/completion observation
  before sending that member's initial instruction exactly once.
- Each configured member receives one distinct provider session with its own identity, worktree, permissions,
  model, effort, event stream, and conversation context.
- The runtime owns and retires all sessions and its shared provider connection. Cancellation or failure partway
  through startup leaks neither registered nor not-yet-published resources, and inconclusive teardown remains
  retryable.
- Existing startup readiness, cancellation, partial-failure, shutdown, session-isolation, and harness-message
  behavior remains unchanged through the black-box acceptance suite.