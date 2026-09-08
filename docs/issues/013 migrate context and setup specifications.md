---
title: Migrate context and setup specifications
priority: 13
---

# Migrate context and setup specifications

This issue implements the [backend test strategy](../manual/test-strategy.md).

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
