---
title: remove backend from config
priority: 1
---

for historical reason the squad config still has "backend" property
it is not needed - remove it

## Analysis

`backend` is a legacy selector for an implementation that is no longer selectable: configuration validation accepts
only `copilot`, and headquarters already composes the Copilot SDK adapter directly. The value is nevertheless copied
through the configuration document, runtime role models, agent-provider context, and the lightweight `squad` command
role model even though no runtime behavior consumes it.

Removing only the JSON property while retaining a hard-coded `"copilot"` value would preserve dead configuration state.
Remove that state end to end while retaining the existing agent-provider abstractions used for runtime ownership and
test substitution. The `agent` object remains the home of `permissions`, `model`, and `effort`, and remains required;
making it optional is a separate configuration-policy change.

## Implementation plan

### Slice 1: Remove the legacy backend selector end to end

1. Remove `backend` from the JSON configuration document and delete its required-value and supported-value validation.
2. Remove the propagated backend/agent-name field from `SquadAgentConfiguration`, `RoleConfigRow`,
   `AgentRoleContext`, and `RoleRow`, then update their construction sites and test support. Do not rename or remove
   the actual agent-backend runtime abstractions: they model lifecycle ownership, not configuration selection.
3. Simplify `SquadConfig.ReadRoles` so role discovery reads only role metadata needed by the `squad` CLI.
4. Remove `backend` from the repository's `blaxquad/squad.json`, README configuration examples, Gherkin fixtures, and
   test-generated configuration helpers. Regenerate committed Reqnroll feature code through the existing build flow
   rather than editing generated files by hand.
5. Run the focused configuration and architecture acceptance scenarios, then the existing build/test suite needed to
   catch constructor and contract changes across projects.

#### Acceptance criteria

- A role configuration containing `name`, `worktree`, and an `agent` object without `backend` parses and launches with
  the same permissions, model, effort, worktree, and receive-mode behavior as before.
- `backend` is no longer part of the supported JSON schema; existing strict unknown-property handling applies if it is
  supplied.
- No backend selector value is carried through configuration, runtime role context, or CLI role metadata.
- Copilot SDK runtime composition and the replaceable agent-backend abstractions remain unchanged.
- Shipped configuration, documentation, and acceptance fixtures use the new schema consistently.
- Existing black-box configuration, startup, handoff, context, queue, and runtime behavior remains green.
