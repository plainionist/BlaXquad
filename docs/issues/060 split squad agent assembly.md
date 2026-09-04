---
title: Split squad agent assembly
priority: 2
---

`squad.Agent` is currently the shared dependency for several unrelated responsibilities: Git and project discovery,
role configuration, handoff file handling, process execution, deployment-tool lookup, and CLI exit signaling. The
assembly is dependency-free and agent-safe, but its broad surface means a consumer can reference substantially more
behavior than it needs.

## Analysis

The current assembly is a useful boundary for the `squad` CLI, but it is not a cohesive module. Its types fall into
five groups with distinct reasons to change:

1. Process execution: `ProcessRunner` and `ProcessResult`.
2. Project and role configuration: `ProjectRoot`, `SquadConfig`, `CurrentRoleResolver`, and `RoleRow`.
3. Handoff protocol and queue state: `HandoffHeaders`, `HandoffQueue`, `Priority`, `Timestamps`, and
   `SequenceCounter`.
4. Published-tool layout: `SiblingTool`.
5. CLI boundary behavior: `CliExitException`.

Do not split solely to reduce the number of files in one project. The goal is to make dependency ownership visible and
to prevent unrelated consumers such as Photino or headquarters from taking a dependency on the complete agent
utility surface.

Keep the new assemblies agent-safe: they must not depend on `squad.Core`, `squad-hq`, Photino, the Copilot SDK, UI
abstractions, or hosting abstractions. The `squad` executable must retain access to all agent commands after the
split.

## Proposed assemblies

### `squad.Agent.Process`

Own external process execution and its result value:

- `ProcessRunner`
- `ProcessResult`

`squad.Agent.Configuration` references this assembly because project-root discovery invokes Git.

### `squad.Agent.Configuration`

Own squad repository discovery and role configuration:

- `ProjectRoot`
- `SquadConfig`
- `CurrentRoleResolver`
- `RoleRow`

Keep `RoleRow` here initially. It is the output of configuration discovery, and a separate contracts assembly would
add another package boundary without resolving a current dependency problem.

### `squad.Agent.Handoff`

Own the on-disk handoff format, queue enumeration, and handoff metadata:

- `HandoffHeaders`
- `HandoffQueue`
- `Priority`
- `Timestamps`
- `SequenceCounter`

`SequenceCounter` belongs with handoff state because it allocates unique handoff sequence values, rather than being a
general-purpose runtime utility.

### `squad.Agent.Tooling`

Own lookup of tools shipped beside the application:

- `SiblingTool`

This keeps publish-directory knowledge separate from agent configuration and handoff behavior.

### `squad.Agent.Cli`

Own the command-line error boundary:

- `CliExitException`

Both executable entry points catch this exception, but it is not a process-execution concern and should not be placed
in `squad.Agent.Process`.

## Dependency direction

```text
squad --------------------> squad.Agent.Configuration, squad.Agent.Handoff,
                            squad.Agent.Process, squad.Agent.Cli
squad-hq ------------------> squad.Agent.Configuration, squad.Agent.Handoff,
                            squad.Agent.Process, squad.Agent.Tooling, squad.Agent.Cli
squad.Core -----------------> squad.Agent.Configuration, squad.Agent.Handoff
squad.Photino --------------> squad.Agent.Process
squad.Agent.Configuration --> squad.Agent.Process, squad.Agent.Cli
squad.Agent.Handoff --------> squad.Agent.Cli
```

The new assemblies must not reference each other in a cycle. In particular, none of them may reference `squad.Core`.
The architecture rule currently verified in `ArchitectureSteps` should evolve with each slice so every intermediate
commit is green, and finish by validating the complete graph rather than a single `squad.Agent` assembly.

## Implementation plan

### Slice 1: Extract the CLI exit contract

Status: complete (6d645a67a1)

1. Create `squad.Agent.Cli` and move `CliExitException`.
2. Update the two executable catch boundaries plus configuration, handoff, and headquarters callers to reference the
   new namespace and assembly directly.
3. Update solution membership and architecture assertions for the extracted contract.

### Slice 2: Extract process execution

Status: complete (bb01e9fa8c)

1. Create `squad.Agent.Process` and move `ProcessRunner` and `ProcessResult`.
2. Update project references and namespaces in the CLI, headquarters, Photino, and configuration code.
3. Update architecture assertions and verify Git discovery and external-tool execution behavior.

### Slice 3: Extract configuration

1. Create `squad.Agent.Configuration` and move project and role discovery types.
2. Reference `squad.Agent.Process` for Git invocation and `squad.Agent.Cli` for user-facing resolution failures.
3. Update CLI commands, headquarters workspace setup, `squad.Core`, and configuration test support.
4. Update architecture assertions and preserve project-root, role parsing, and current-role resolution behavior.

### Slice 4: Extract handoff behavior

1. Create `squad.Agent.Handoff` and move the handoff types.
2. Reference `squad.Agent.Cli` for ambiguous queue-state failures.
3. Update CLI commands, headquarters timestamp logging, and `squad.Core` handoff delivery consumers.
4. Update architecture assertions and preserve file ordering, header parsing, timestamp formatting, sequence
   allocation, and CLI rendering behavior.

### Slice 5: Extract tooling and remove the umbrella assembly

1. Create `squad.Agent.Tooling`, move `SiblingTool`, and update its headquarters consumer.
2. Remove the now-empty `squad.Agent` project and all remaining project references.
3. Finalize solution membership, architecture tests, documentation, and build/publish checks for the complete graph.
4. Run the focused acceptance scenarios followed by the complete build and test suite.

## Acceptance criteria

- Each proposed assembly has one cohesive responsibility and contains only the types listed for it.
- `squad` retains all existing handoff, context, ready-for-next, and done-with-current commands.
- Project-root, role-resolution, configuration, handoff, process, tool lookup, and CLI exit behavior is unchanged.
- No new assembly references `squad.Core`, `squad-hq`, Photino, the Copilot SDK, UI abstractions, or hosting abstractions.
- Consumers reference the narrowest assembly needed for their behavior.
- `squad.Agent` is removed, unless an external compatibility requirement is discovered and explicitly documented.
- Architecture tests verify the new dependency graph and agent-safe boundary.
- Existing black-box Gherkin acceptance scenarios and the full .NET build/test suite remain green.

## Risks and decisions

- `CliExitException` must move first because both configuration and handoff behavior depend on it; extracting either
  first would retain an inverted dependency on the umbrella assembly or require a temporary cycle.
- Moving `ProcessRunner` affects several projects and must be completed before moving configuration because
  `ProjectRoot` uses it.
- `RoleRow` should not be extracted into a standalone contracts assembly unless a future dependency cycle requires it.
- `HandoffQueue` currently includes stdout rendering as well as file enumeration. Keep those together for this change;
  split presentation later only if a second consumer needs a non-CLI representation.
- If published or external consumers depend on the `squad.Agent` assembly identity, introduce a deliberate compatibility
  package rather than silently retaining an umbrella dependency.