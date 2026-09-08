---
title: Migrate handoff and queue specifications
priority: 14
---

# Migrate handoff and queue specifications

This issue implements the [backend test strategy](../manual/test-strategy.md).

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

## Implementation plan

Migrate the specifications in three sequential slices. Keep the process boundary as the subject of every action and
keep all durable-file knowledge behind test-owned support. Do not change production APIs or migrate the unrelated
delivery-retry scenario in `Recovery.feature`.

### Slice 1: Outbound handoffs

**Status: complete (35bf5c506d)**

- Move `Handoffs.feature` onto the shared configured-project fixture so each role has a realistic Git worktree.
- Run every `handoff` action through the published `squad` executable from the sender's role worktree.
- Introduce a test-owned mailbox observer with semantic results for queued handoffs. Confine directory enumeration and
  parsing of the durable handoff representation to that observer; step definitions must not inspect headers, payload
  separators, file names, or extensions.
- Rewrite assertions in user terms: sender, recipients, priority, type, task, commit-delivery instruction, and note
  message. Keep the draft-removal and aggregated-validation behavior.
- Keep draft creation in test support and isolate its representation so a later JSON/YAML migration changes support,
  not feature language or step definitions.

Acceptance:

- The Git handoff scenario proves a committed change is queued for the reviewer with the expected task and priority.
- The note scenario proves one logical handoff targets both recipients and retains its message.
- The invalid draft reports all repairable errors, remains available for correction, and queues nothing.
- `Handoffs.feature` passes without referencing handoff product types or asserting the persisted serialization format.

### Slice 2: Single-task queue and task recovery

**Status: changes requested (7d0d271470)**

- Move task-role setup onto the shared configured-project fixture while retaining receive-mode, unrelated-directory,
  and ambiguous-worktree arrangements as semantic workspace operations.
- Replace raw task-file creation and lookup in step definitions with test-owned mailbox fixture and observer operations
  for queued, current, and completed tasks. Keep serialization, paths, directory enumeration, and archive-collision
  setup inside that support.
- Run `ready-for-next` and `done-with-current` through the published `squad` executable from the role worktree, except
  scenarios that intentionally run from an unrelated or ambiguous location.
- Migrate the task-recovery and completion-archive-collision scenarios in `Recovery.feature` with the same semantic
  support. Leave the delivery idempotency scenario unchanged.

Acceptance:

- Empty, unrelated-directory, ambiguous-role, and empty-receive-mode outcomes remain covered.
- Priority selection, immediate next-task acceptance, and multiple-current-task rejection remain covered through
  semantic mailbox state.
- A current task remains current across a new command invocation, and an archive collision fails without losing it.
- The task scenarios in `TaskQueue.feature` and `Recovery.feature` pass without constructing configuration rows,
  workspace context, queue objects, or command implementations.

#### Review findings on 7d0d271470

**Finding 1 — high**

- **Location:** `src/squad.Specs/StepDefinitions/QueueSteps.cs` (`GivenANestedDirectoryExists`,
  `WhenTheNestedDirectoryChecksForWork`), `src/squad.Specs/Features/TaskQueue.feature` (unrelated nested directory
  scenario).
- **Violated behavior:** Slice 2 must keep the unrelated-directory outcome covered as a semantic workspace operation,
  distinct from an empty queue, and must run that scenario from a location that is not the task role's worktree.
  Step definitions must not inspect `RoleWorktreePath`.
- **Root cause:** Task-role setup now uses `ConfigureProject`, so the role lives at `.worktrees/reviewer`. A nested
  directory under the main checkout would no longer match that worktree. The steps create `nested/current` inside
  `RoleWorktreePath("reviewer")` so `ready-for-next` still resolves the role and returns `NO_TASK`. That is the empty
  queue already covered by "An empty queue has no task", not an unrelated location.
- **Required outcome:** Arrange the unrelated directory through a semantic workspace operation that does not expose
  worktree paths or hard-code the role name in step definitions. The scenario must fail if the command resolves the
  task role from that location. Do not place the directory inside the role worktree merely to keep empty-queue
  `NO_TASK` green.

**Finding 2 — medium**

- **Location:** `src/squad.Specs/StepDefinitions/QueueSteps.cs` (`GivenAGitProjectWithRoleAndAnEmptyReceiveMode`,
  `GivenAGitProjectWithTwoRolesSharingTheCurrentWorktree`).
- **Violated behavior:** Receive-mode and ambiguous-worktree arrangements must be semantic workspace operations.
  Step definitions must not write `squad.json` or other durable configuration representation.
- **Root cause:** After moving the happy-path task role onto `ConfigureProject`, these two Givens still embed the
  squad configuration document (role names, `worktree`, `receiveMode`) in the step class instead of a workspace
  operation comparable to `ConfigureProject`.
- **Required outcome:** Empty receive mode and two roles sharing the current worktree are arranged by test-owned
  workspace APIs. Step definitions name the arrangement; they do not serialize configuration.

### Slice 3: Batch queue

- Reuse the task mailbox fixture and observer from slice 2 to arrange and inspect batch state without exposing durable
  representation in steps.
- Run batch `ready-for-next` and `done-with-current` actions through the published `squad` executable from the batch
  role's worktree.
- Express current-batch and completed-batch observations semantically, including membership and priority, rather than
  inferring them from directory or file layout.

Acceptance:

- Empty queue, equal-priority grouping, lower-priority deferral, batch completion followed by next-batch acceptance,
  and rejection of a single current task all remain covered.
- `BatchQueue.feature` passes without direct product API use or persisted-format assertions.
- The complete migrated feature set passes together, and direct handoff/configuration/queue implementation references
  used only by these scenarios are removed.
