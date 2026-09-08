---
title: Migrate delivery and recovery specifications
priority: 16
---

# Migrate delivery and recovery specifications

This issue implements the [backend test strategy](../manual/test-strategy.md).

## Goal

Rewrite handoff delivery and recovery scenarios as complete user flows through real `squad` and `squad-hq` processes,
the real filesystem queue, and fake provider sessions.

Migrate:

- `Delivery.feature`;
- the duplicate-delivery scenario in `Recovery.feature`; and
- the in-process polling and recovery scenarios currently embedded in `ViewModel.feature`.

The observable result is durable delivery and recipient behavior, not a notifier invocation or poller method call.

## Acceptance criteria

- A handoff created through `squad` is delivered by a running `squad-hq` process to the intended recipient mailbox.
- The recipient fake session observes the real wake-up/harness message.
- Invalid fan-out does not leave any recipient with a partial delivery.
- A notification failure does not lose a durably delivered handoff.
- Retrying an already persisted delivery does not create a duplicate.
- Existing inbox work is recovered without mutation and wakes a replacement/available session where supported.
- Unavailable or busy recipients preserve work for later delivery according to the supported behavior.
- Scenarios observe filesystem state semantically and fake-agent input; they do not inspect notifier call logs.
- Step definitions do not construct `InProcessHandoffPoller`, `HandoffDeliveryService`, `IRoleNotifier`, or role-row
  product types.
- Poller cancellation or call-order scenarios without a process-visible invariant are deleted.

## Implementation plan

Keep the production delivery implementation unchanged unless a black-box scenario exposes a product defect. Extend the
existing `BackendScenario` test facade rather than adding a second process driver: it remains the single owner of the
real `squad-hq` process, fake-provider control channel, and bounded waits, while `ScenarioWorkspace` and mailbox support
hide filesystem formats and real `squad` command invocation from step definitions.

### Slice 1 - Process-level handoff delivery (in progress)

**Status: changes requested (d7643b4350)**

- Rewrite `Delivery.feature` around configured Git worktrees, the published `squad` and provider-free `squad-hq`
  executables, stdio UI startup, and the fake-provider control pipe.
- Cover successful delivery by queuing a note through `squad`, waiting for the recipient fake session to observe the
  real wake-up harness message, and asserting semantic sender-archive and recipient-inbox state.
- Cover invalid fan-out by seeding only the otherwise-uncreatable invalid durable prerequisite through test-owned
  mailbox support, then proving the real host rejects it before any recipient receives a copy or wake-up.
- Add a fake-provider command that makes the recipient reject the next host-authored harness send. Use it to prove
  notification failure leaves the recipient copy durable, archives the sender copy as sent, and does not terminate the
  headquarters process. Do not inspect the delivery log or notifier calls.
- Extend mailbox support with semantic archive/inbox observations and prerequisite seeding; keep paths, handoff headers,
  and file contents out of step definitions. Remove `DeliverySteps` dependencies on delivery product types and delete
  `RecordingRoleNotifier` when no scenario uses it.

Acceptance criteria:

- Every `Delivery.feature` scenario crosses the real CLI/process/filesystem/provider boundaries and shuts headquarters
  down through host control.
- The happy path's recipient fake session observes the installed `ready-for-next` wake-up harness message.
- Invalid fan-out produces a failed sender artifact with no partial recipient copy and no delivery wake-up.
- A failed harness send still leaves exactly one recipient copy and one sent sender artifact while headquarters remains
  available.

#### Review findings on d7643b4350

**Finding 1 — high**

- **Location:** `src/squad.Specs/StepDefinitions/DeliverySteps.cs`
  (`ThenTheSenderHandoffIsArchivedAsSent`), `src/squad.Specs/Features/Delivery.feature`
  (Deliver a handoff and notify its recipient; Notification failure does not lose a delivered handoff).
- **Violated behavior:** Slice 1 requires a failed harness send to leave exactly one sent sender artifact. The Then
  "the sender handoff is archived as sent" must distinguish sent from failed.
- **Root cause:** The step waits until `SentHandoffs + FailedHandoffs == 1` and returns. The matching failed step then
  asserts `FailedHandoffs` has exactly one item; the sent step never asserts `SentHandoffs`. The pre-migration step
  asserted a sent archive of length 1. A delivery that archives as failed can satisfy this named step.
- **Required outcome:** After waiting for the outbound artifact to leave the outbox, assert exactly one sent handoff
  and no failed archive for that sender.

**Finding 2 — high**

- **Location:** `src/squad.Specs/StepDefinitions/DeliverySteps.cs`
  (`ThenTheAgentObservesTheHandoffWakeUpMessage`), `src/squad.Specs/Features/Delivery.feature` (all three scenarios).
- **Violated behavior:** The happy path's recipient fake session must observe the installed `ready-for-next` wake-up
  harness message. Invalid fan-out must produce no delivery wake-up. A failed harness send must be proven as a
  notification failure without inspecting the delivery log or notifier calls. Scenarios observe fake-agent input.
- **Root cause:** The happy path waits on `WaitForTranscriptAsync` (UI protocol) instead of the recipient fake
  session's harness-message observation. Invalid fan-out and notification-failure assert only mailbox state and host
  liveness. Notification-failure assertions are a subset of the happy path, so a no-op `reject-next-harness` still
  passes. The original wake-up presence/absence coverage was not replaced with fake-session observation.
- **Required outcome:** On the happy path, wait for the recipient fake session's harness-message observation and
  assert it is the installed `ready-for-next` wake-up. After invalid fan-out, prove that session did not observe a
  delivery wake-up. After a rejected harness send, prove that session did not observe the wake-up while still
  asserting one sent artifact, one recipient copy, and headquarters remaining available.

### Slice 2 - Idempotent delivery and restart recovery (queued)

- Migrate the duplicate-delivery scenario in `Recovery.feature` to the process facade. Queue the outbound handoff through
  `squad`, seed a matching already-persisted recipient copy through mailbox support, run headquarters, and prove retry
  archives the sender copy without creating or mutating a second recipient copy.
- Add semantic mailbox fixtures and snapshots for existing `new` and `in_process` recipient work. Restart headquarters
  against the same workspace with a fresh fake-provider session, then prove startup recovery preserves the exact durable
  entries and sends one recovery wake-up to the replacement session.
- Make restart and harness-message observation explicit facade operations with bounded diagnostic waits; do not expose
  process handles, named-pipe DTOs, raw paths, or product delivery objects to steps.

Acceptance criteria:

- Retrying an already persisted delivery leaves exactly one byte-for-byte unchanged recipient copy.
- Both `new` and `in_process` work survive a real headquarters stop/restart unchanged.
- The replacement fake session receives one recovery wake-up after its normal initial harness instruction.

### Slice 3 - Busy and unavailable recipients; obsolete-spec cleanup (queued)

- Move the process-visible intent of the in-process polling scenarios from `ViewModel.feature` into delivery/recovery
  features. Keep a recipient busy with a real UI prompt and delayed fake-agent reply, queue a handoff through `squad`,
  and prove the durable delivery exists before the serialized wake-up reaches the recipient after its prompt completes.
- Exercise the supported unavailable-recipient path through fake-provider lifecycle/failure controls and prove work
  remains in the recipient mailbox for a later available session, which receives the recovery wake-up.
- Delete the caller-cancellation scenario and handoff-specific lifecycle call-order assertions that have no
  process-visible invariant. Remove their steps, fields, helpers, and now-unused recording support from
  `ViewModelSteps`.
- Search all step definitions and ensure none construct or name `InProcessHandoffPoller`,
  `HandoffDeliveryService`, `IRoleNotifier`, or delivery role-row product types.

Acceptance criteria:

- Busy-recipient delivery is durable before notification and the fake session observes no overlapping prompt/harness
  operation.
- Unavailable-recipient work survives until a later available session is started and woken.
- No in-process polling/cancellation scenario remains, and all migrated delivery/recovery steps use only test-owned
  semantic APIs.

For each slice, run the focused `Delivery.feature`, `Recovery.feature`, and affected `ViewModel.feature` scenarios through
the repository's existing backend-spec test command; run the full `squad.Specs` suite after the final cleanup slice.
