---
title: Support sequential roles sharing a worktree
priority: 110
---

# Support sequential roles sharing a worktree

## Problem

BlaXquad requires every role to resolve to a unique worktree. That prevents an intentional serial workflow in which
architect, coder, and reviewer operate in the same main checkout so each role immediately sees the previous role's
committed and uncommitted changes.

The special worktree name `master` means the physical main checkout, not merely a branch from which dedicated
worktrees are created. Multiple roles can use that checkout safely only when headquarters guarantees that no two
agent operations can use it at the same time.

Removing the duplicate-`master` validation is not sufficient:

- `SquadConfigurationLoader` also rejects duplicate normalized worktree paths.
- `CurrentRoleResolver` derives role identity only from the current worktree path and rejects multiple matches.
- `context`, `handoff`, `ready-for-next`, and `done-with-current` therefore cannot identify a role in a shared
  checkout.
- Handoff inbox, outbox, sent, failed, in-process, and completed state is namespaced by worktree rather than role.
- Recovery treats any pending inbox entry as mail for every role mapped to that worktree.
- Runtime prompt and operation serialization is per role. Session creation and initial harness prompts can run in
  parallel, and a recipient wake-up can overlap the sender's active operation.
- The current reviewer workflow merges accepted work to master. If the coder works directly on master, the commit is
  already integrated before review and that gate has different semantics.

The SDK sessions, dashboard routing, transcript state, host lease, and basic workspace preparation are already keyed
by role or project and do not inherently require distinct worktree paths.

## Goal

Allow explicitly configured roles to share one checkout while preserving exact role identity, isolated durable
handoff state, and exclusive access to that checkout. Roles on different worktrees must retain their current ability
to operate independently.

Shared-worktree safety must be an authoritative C# invariant. Role prompts may describe the workflow, but prompt
conventions and expected agent behavior must not be the concurrency boundary.

## Required design

### Explicit sharing contract

Add an explicit configuration contract for sequentially shared worktrees. Accidental duplicate paths must remain a
configuration error. Normalize resolved paths and treat all roles assigned to the same path as one execution group;
do not special-case only the literal name `master` if the same invariant applies to another shared checkout.

Configuration diagnostics must distinguish an accidental duplicate, an invalid sharing declaration, and a sharing
group that cannot be scheduled safely.

### Session-bound role identity

Decouple the active role from worktree-path uniqueness. Every agent session must receive a role identity that the
`squad` CLI can resolve when launched from a shared checkout. The CLI must validate that the supplied session role is
configured for the current checkout.

Do not use a mutable process-global current role. An explicit command argument, provider-session environment, or
equivalent session-scoped mechanism is acceptable only if it works for every helper command and cannot leak from one
role operation into another. A manual invocation from an ambiguous shared checkout without role context must fail
with a controlled diagnostic.

### Role-keyed handoff persistence

Address durable handoff state by role independently of its worktree path. Separate each role's inbox and task state,
and retain sender ownership for outbox, sent, and failed artifacts. Delivery to two recipients that share a checkout
must create two independently consumable recipient artifacts.

`ready-for-next` and `done-with-current` must operate only on the active role's state. Launch-scoped handoff cleanup
must discard only the active role's own state without cross-role consumption or silent loss of another role's queue.
Existing unique-worktree state must remain compatible or have one explicit migration path.

### Worktree-level operation admission

Serialize provider operations by normalized worktree path in addition to existing per-role coordination. The
exclusive interval must cover every operation during which an agent can read or mutate the checkout, including
initial harness prompts, UI prompts, handoff wake-ups, retries, cancellation, relaunch, and teardown.

A role waiting for a shared checkout must not hold lifecycle resources that prevent the active role from completing
or being cancelled. Roles assigned to distinct paths must not be serialized behind one global lock. Ordering and
release must be based on observable provider operation completion rather than sleeps or assumed response timing.

### Integration and review semantics

Define the supported Git contract for a shared main checkout. In particular, decide whether review is advisory after
a coder commit has advanced master, whether rejected work is reverted or amended in place, or whether another
integration mechanism preserves review-before-master. Update the constitution and role prompts so they do not ask a
reviewer to merge a commit that is already on master.

The feature does not need to permit concurrent mutation of one checkout or bypass normal Git index and ref safety.

## Implementation plan

### Slice 1: Specify shared-worktree identity and persistence

1. Add black-box scenarios for an explicitly shared checkout, ambiguous manual CLI invocation, role-specific CLI
   context, isolated task queues, multi-recipient delivery, and recovery.
2. Introduce the configuration model for intentional sharing while retaining unique worktrees as the default.
3. Establish one session-scoped role identity contract used by all `squad` commands and validate it against the
   current worktree.
4. Move handoff path ownership behind one role-aware API and update command-side queue operations, delivery,
   recovery, preparation, cleanup, and continuation through that API.
5. Preserve the existing on-disk contract for unique worktrees where practical; otherwise implement and specify an
   explicit migration with no silent task loss.

### Slice 2: Enforce shared-checkout sequencing

1. Add a worktree-group operation coordinator at the runtime boundary rather than duplicating checks in individual
   UI and handoff call sites.
2. Route initial harness prompts, manual prompts, handoff notifications, aborts, and relaunch through the same
   admission authority.
3. Add a controllable-provider scenario proving that two roles on one checkout never have overlapping provider
   operations, including startup and a handoff sent before the sender becomes idle.
4. Prove separately that roles on distinct worktrees can still operate concurrently.
5. Define cancellation and shutdown behavior for both the active role and roles waiting for the checkout.

### Slice 3: Complete the serial master workflow

1. Exercise the architect-to-coder-to-reviewer workflow through the real process boundary with all roles assigned to
   the main checkout.
2. Specify and implement the chosen commit, rejection, acceptance, and integration behavior.
3. Update the manual, glossary, example configuration, constitution, and role prompts to describe shared versus
   isolated worktree operation accurately.
4. Keep the existing distinct-worktree workflow and its handoff behavior supported.

## Acceptance criteria

- A configuration can explicitly assign architect, coder, and reviewer to the main checkout and headquarters starts
  successfully.
- The same duplicate path without the explicit sharing contract remains a configuration error.
- Each shared-checkout session obtains its own role from `squad context`, and an ambiguous manual invocation fails
  without guessing.
- `handoff`, `ready-for-next`, and `done-with-current` act on the calling role even when every role has the same
  worktree path.
- Pending, in-process, completed, sent, and failed handoffs cannot be observed or consumed as another role's state.
- A multi-recipient handoff creates one durable recipient artifact per role even when recipients share a checkout.
- Fresh and continued launch both clear each role's handoff state according to the existing launch-scoped lifecycle
  contract; `--continue` preserves only worktree (Git) content, never queued handoffs.
- At most one provider operation can use a normalized shared worktree path at a time, including during startup,
  wake-up, cancellation, and relaunch.
- Provider operations using different worktree paths remain independently executable.
- The review and integration workflow has explicit behavior for accepted and rejected commits already created in the
  main checkout; no role is instructed to perform a meaningless merge.
- The behavior is covered through the existing black-box Gherkin acceptance suite using real CLI and headquarters
  process boundaries. No timing sleeps, prompt-only exclusion guarantee, process-global mutable role, or test-only
  production branch is introduced.

## Non-goals

- Running multiple mutating agents concurrently in one checkout.
- Forcing one Git branch to be checked out in multiple linked worktrees.
- Replacing isolated worktrees for teams that need concurrent role execution.