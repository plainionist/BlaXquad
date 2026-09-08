---
title: Migrate context and setup specifications
priority: 13
---

# Migrate context and setup specifications

This issue implements the [backend test strategy](../manual/test-strategy.md).

## Architecture

Keep context coverage at the published CLI boundary. `ScenarioWorkspace` remains the single owner of temporary Git
repository setup, role worktree paths, environment construction, and exact executable selection. Extend that
test-owned API with role-oriented `squad` invocation rather than letting `ContextSteps` create worktrees, construct
configuration files, retain raw path maps, or launch tools directly.

The scenarios assert the `context` command's public contract: exit status, scalar standard output, and the documented
JSON fields. JSON deserialization remains test code and must not use `Ctx`, `SquadConfig`, or any product workspace
type. Keep both existing scenarios because together they cover worktree-derived identity and the complete documented
machine-readable context, including `BLAXQUAD_SRC`.

`Configuration.feature` and `Startup.feature` are empty placeholders. Their former parser/preparer scenarios and
test-only commands were already removed; the bindings that remain perform only unreachable fixture setup or direct
product inspection. Delete those features, generated fixtures, and bindings rather than restoring white-box
configuration coverage. Configuration failures still exercised by supported commands elsewhere remain out of scope.

## Implementation plan

Implement this issue as one independently reviewable slice.

### Slice 1: Migrate context specifications and remove empty setup fixtures

**Status: in progress**

1. Extend `ScenarioWorkspace` so one configured-project operation creates the real Git repository and all requested
   role worktrees, records their locations behind the workspace API, and can run the exact published `squad`
   executable for a named role with an optional test-owned environment.
2. Refactor `ContextSteps` to use that API. Preserve both scenarios and assert successful exit plus the exact role
   output or documented JSON fields; do not instantiate or inspect configuration, context, or workspace product
   objects.
3. Delete `Configuration.feature`, `Startup.feature`, their generated `.feature.cs` fixtures, and
   `ConfigurationSteps.cs`. Do not retain unused setup/assertion bindings and do not add replacement parser or
   preparer tests.
4. Regenerate the `Context.feature` fixture through the existing Reqnroll build path and run the focused context
   specification, followed by the existing backend acceptance suite if the focused migration passes.

**Slice acceptance**

- Both context scenarios execute the exact `squad-tools` publication from temporary real role worktrees created by
  `ScenarioWorkspace`.
- Role resolution succeeds without `BLAXQUAD_ROLE`, and JSON output identifies the role, project root, role worktree
  root, and explicit shared source path.
- Context step definitions contain no Git commands, configuration-file construction, worktree bookkeeping, direct
  process setup, or product configuration/context types.
- Empty setup features, generated fixtures, and all bindings used only by them are absent.
- No production project or API changes, test-only production branches, or replacement test assembly are introduced.
- The supported backend acceptance suite passes with no context or configuration behavior regression.

## Goal

Make project setup and context scenarios the first existing specifications to use the process driver.

Refactor the supported scenarios in `Context.feature` to run the actual `squad` executable in temporary real Git
repositories and worktrees. Consolidate duplicated repository, configuration, role, prompt, and executable setup behind
the test-owned workspace API.

`Configuration.feature` and `Startup.feature` currently contain no scenarios. Delete those placeholders and their
orphaned bindings. If an existing setup step protects a real user-visible failure, rewrite that behavior as a process
scenario rather than retaining a parser or preparer test.

## Acceptance criteria

- `Context.feature` exercises only the published `squad` executable.
- Context assertions use exit status and documented command output, not `Ctx`, configuration, or workspace product
  objects.
- Temporary repositories and role worktrees are created through one test-owned workspace API.
- Feature steps contain no raw product setup or process-launching details.
- Empty configuration/startup features, generated fixtures, and unreachable step definitions are removed.
- No supported configuration or context behavior is lost; any retained setup failure is observable through an actual
  command.
- No production API or test-only branch is introduced.
