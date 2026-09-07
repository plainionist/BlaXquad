---
title: Migrate handoff and queue specifications
priority: 14
---

# Migrate handoff and queue specifications

This issue implements the [backend test strategy](../manual/test-strategy.md).

## Dependency

- `013 migrate context and setup specifications.md`

## Goal

Refactor the existing agent-command use cases to invoke the real `squad` executable and observe durable mailbox
behavior without calling handoff, configuration, or queue implementation APIs.

Migrate:

- `Handoffs.feature`;
- `TaskQueue.feature`;
- `BatchQueue.feature`; and
- the task/batch recovery scenarios in `Recovery.feature`.

Most scenario intent is already user-oriented and should be retained. Rewrite steps that fabricate or inspect product
objects. Delete only duplicate assertions that do not add a supported behavior.

## Acceptance criteria

- Every migrated action runs the published `squad` executable from a realistic main checkout or role worktree.
- Scenarios retain handoff validation, multi-recipient delivery intent, priority ordering, task/batch transitions,
  ambiguity handling, completion, and restart recovery behavior.
- Step definitions use test-owned semantic values such as handoff summary, mailbox state, current task, and current
  batch.
- Raw handoff parsing and directory enumeration are confined to the test-owned workspace observer when durable state
  must be inspected.
- Scenarios do not instantiate `HandoffQueue`, configuration rows, workspace context, or command implementation types.
- The tests do not freeze the current handoff serialization format and remain compatible with
  `json for handoffs.md`.
- No product API is widened for fixture setup or observation.
