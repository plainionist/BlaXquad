---
title: handoff cleanup
priority: 1
---

handoff documents do not need versioning & LegacyHandoffQueueGuard is not needed
because on "launch" we simply clear all remaining handoffs

==> add this cleanup at startup
==> remove the now dead code mentioned above

# Handoff cleanup

## Architectural decision

The current repository contract conflicts with this issue: `--continue` preserves handoff queues, startup recovers
pending inbox work, handoff JSON carries `schemaVersion`, and legacy `.handoff` artifacts block both continued launch
and role commands.

This issue replaces that contract:

- Every `squad-hq launch`, including `--continue`, removes the complete `.blaxquad/handoffs` state for each distinct
  configured worktree before agent sessions and handoff delivery start.
- `--continue` continues to preserve Git worktree contents, but it does not preserve handoffs.
- Handoffs created after startup remain typed, strictly validated `.handoff.json` documents and retain their existing
  atomic delivery, collision, and lifecycle behavior within that Headquarters run.
- Handoff documents have no schema-version field. A process launch is the format boundary, so no persisted handoff
  needs to cross executable versions.
- Legacy `.handoff` files need neither migration nor a runtime guard: launch cleanup discards them with the rest of
  the queue.

## Implementation plan

### Slice 1: Make handoff state launch-scoped

1. Change workspace launch preparation so both normal and `--continue` launches delete each distinct configured
   worktree's complete handoff-state directory, then recreate the canonical queue directories. Keep normal launch's
   dedicated-worktree reset and `--continue`'s worktree preservation unchanged.
2. Perform cleanup before sessions, recovery notifications, or delivery polling can observe stale files. Remove the
   now-unreachable startup handoff-recovery operation from the runtime controller, poller, and delivery service while
   retaining same-run idempotent recipient writes and notification-failure handling.
3. Replace the cross-launch recovery scenarios and the continued-launch legacy-queue failure scenarios with focused
   black-box acceptance coverage proving that normal and continued launch clear outbox, archive, inbox, and nested
   batch state without delivering or waking a role for stale work. Prove in the continued-launch case that an
   unrelated worktree file remains intact.
4. Remove obsolete recovery-only bindings and fixture helpers, and update scenario-support comments to describe
   launch-scoped queues.
5. Update `README.md`, the architecture, glossary, module inventory, and test strategy so `--continue` promises only
   worktree preservation and handoffs are file-backed state for the current Headquarters run rather than
   restart-safe state. Align the conflicting handoff-preservation criterion in
   `docs/issues/023 support sequential roles sharing a worktree.md`.

Acceptance criteria:

- Both launch modes remove every file and batch directory beneath each configured handoff-state root before the
  handoff pump starts.
- A continued launch preserves non-handoff worktree content while discarding queued, in-process, completed, sent,
  and failed handoffs.
- No stale handoff is delivered and no recovery wake-up is emitted after process launch.
- Handoffs created after startup still deliver, notify, claim, batch, and complete as before.

### Slice 2: Remove handoff document versioning

1. Remove `CurrentSchemaVersion`, `SchemaVersion`, its validation branch, and producer initialization from the shared
   handoff document model and CLI.
2. Keep strict JSON parsing and all structural, recipient, kind/variant, priority, and lifecycle validation intact;
   update format-error documentation so it describes only supported validation failures.
3. Convert all black-box mailbox fixtures and raw handoff examples to the unversioned shape, delete the obsolete
   unsupported-version scenario, and retain malformed-JSON and mismatched-variant coverage.
4. Update the architecture, glossary, and module inventory to describe one typed JSON representation without a
   version negotiation or upgrade contract.

Acceptance criteria:

- Newly queued and delivered handoffs contain no `schemaVersion` property.
- Versionless handoffs pass through delivery and task/batch lifecycle transitions without losing typed data or
  timestamps.
- Malformed JSON and invalid kind/variant documents still fail explicitly before any recipient copy is delivered.
- No production or acceptance-support code references a handoff schema version.

### Slice 3: Remove legacy-queue guarding

1. Remove `LegacyHandoffQueueGuard` and `LegacyHandoffQueueException`.
2. Remove guard invocations and exception handling from handoff creation and task/batch claim and completion commands;
   keep each command's existing validation and diagnostics unchanged.
3. Delete the obsolete role-command legacy rejection scenario and its legacy-artifact setup binding. Remove remaining
   legacy migration, mixed-queue, and guard documentation.
4. Run the focused handoff, delivery, task-queue, batch-queue, and launch-lifecycle acceptance features together to
   protect the supported workflow after the shared guard disappears.

Acceptance criteria:

- Legacy artifacts present before startup are removed by launch cleanup and never block Headquarters startup.
- Supported role commands operate solely on `.handoff.json` queue entries and retain their existing success and
  ambiguity/error behavior.
- The legacy guard, its custom exception, its diagnostics, and all production, acceptance-support, and stable-manual
  references are gone.
- The repository builds and the affected black-box acceptance features pass after this slice independently.
