---
title: Establish the process-level backend specification driver
priority: 12
---

# Establish the process-level backend specification driver

This issue implements the [backend test strategy](../manual/test-strategy.md).

## Dependencies

- `010 make agent provider selectable.md`
- `011 add headless ui protocol host.md`

## Goal

Prove the complete target architecture and establish the single test-owned API that later specification migrations
will use.

Keep all support in `squad.Specs`. Add:

- a scenario driver for the real `squad` and `squad-hq` processes;
- a headless UI JSON client;
- a fake provider implementing the production provider SPI; and
- a private named-pipe control channel between the test runner and the fake provider loaded in the headquarters
  process.

The driver exposes user-oriented workspace, CLI, UI, agent, and lifecycle operations. Step definitions must not see
provider event records, protocol plumbing, child-process details, or product objects.

## Acceptance criteria

- The fake provider and all of its control-channel code live in `squad.Specs`; no test assembly is added.
- The fake provider is loaded into the real `squad-hq` process through the provider SPI.
- The published headquarters process used by the proof neither references nor contains `squad.CopilotSdk`.
- The driver completes the real UI-ready handshake over stdin/stdout.
- A configured fake role session starts through the normal headquarters lifecycle.
- A prompt sent through the UI JSON protocol reaches the fake provider.
- An assistant reply emitted through the fake-provider control channel appears as a real transcript protocol message.
- Shutdown is requested through the actual `squad-hq shutdown` command and the host exits cleanly.
- All waits use observable acknowledgements or state with bounded diagnostic timeouts; no arbitrary synchronization
  sleeps are introduced.
- A failed scenario performs bounded cleanup and reports captured process, UI, and provider state.
- Existing specifications remain in `squad.Specs` and continue to run while migration is in progress.
