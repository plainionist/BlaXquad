---
title: Remove the legacy white-box test harness
priority: 21
---

# Remove the legacy white-box test harness

This issue implements the [backend test strategy](../manual/test-strategy.md).

## Goal

Delete the obsolete white-box step definitions, fixtures, fakes, helpers, and project references after all supported
scenarios use the process-level test architecture.

Keep only the process driver, headless UI client, fake provider/control channel, semantic workspace observers, and
small helpers directly owned by current scenarios.

## Acceptance criteria

- `ViewModelSteps`, direct protocol/publisher steps, direct delivery steps, and direct host-control steps are removed or
  contain only process-driver calls.
- `LifecycleTrace`, `RecordingPhotinoUi`, `RecordingWindowHost`, `RecordingSleepInhibitor`,
  `RecordingHandoffPump`, `RecordingHostLease`, `FaultingHostLease`, `RecordingRoleNotifier`, and obsolete recording
  provider types are removed.
- The retained fake provider is purpose-built for the provider SPI and controlled only through the scenario driver.
- `squad.Specs` has no compile-time product reference except `squad.AgentProvider.Abstractions`, which is required to
  implement the fake provider plug-in.
- `squad.Specs` does not reference the `squad` or `squad-hq` projects as assemblies; publishing them remains a build
  dependency.
- No step definition imports or names a product implementation type.
- No empty feature, generated orphan fixture, unreachable binding, or duplicate setup helper remains.
- All retained scenarios run through actual published processes and pass independently and as a complete suite.
