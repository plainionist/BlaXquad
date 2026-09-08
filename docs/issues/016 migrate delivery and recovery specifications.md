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
