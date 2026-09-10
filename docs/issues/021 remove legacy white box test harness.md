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

## Implementation plan

Retain the published `squad.exe` and provider-free `squad-hq.exe` as the only product boundary used by
`squad.Specs`. Step definitions describe behavior and call test-owned scenario facades; process startup, JSON framing,
fake-provider control, host-control commands, and filesystem setup stay behind those facades. The provider-selection
fixtures that intentionally exercise valid, incompatible, and throwing plug-in descriptors remain narrow test inputs,
not alternate runtime harnesses.

### Slice 1: Drive active usage through the shared fake-provider scenario

1. Extend the existing fake-provider control protocol and `BackendScenario`/`BackendScenarioAgent` facade with
   semantic operations for publishing context/AIC usage while a send remains active and for completing that send with
   final usage and idle.
2. Migrate `ActiveUsageRefresh.feature` bindings to configure, launch, command, and observe the published process only
   through `BackendScenario`; keep its ordering assertions that live usage reaches the UI before idle and that idle
   preserves the latest values.
3. Delete `ControllableAgentProviderFactory`, its backend/runtime/session types, `UsageControlChannel`, and the
   duplicate process/stdin/stdout management in `ActiveUsageRefreshSteps`.
4. Run `ActiveUsageRefresh` independently together with the fake-provider control and process-driver scenarios that
   protect the extended control protocol.

**Slice acceptance:** The active-usage scenarios retain their current observable ordering through the published
process, while the shared fake provider and scenario facade are their only provider-control and process-driving path.

### Slice 2: Centralize stdio and multi-process scenarios on the process driver

1. Add only the missing raw-wire and lifecycle operations to `BackendScenario` and `HeadlessUiClient`: send malformed
   lines or explicit envelopes, observe typed protocol output and stdout/stderr separation, inspect bounded process
   state, and start/stop independently owned scenario instances.
2. Migrate `StdioUiProtocolSteps`, `HeadlessUiClientSteps`, and `HostCoexistenceSteps` away from their private process
   fields, reader loops, JSON parsing, and setup dictionaries. Keep raw framing assertions in the headless client and
   multi-workspace ownership in test-owned scenario support rather than in bindings.
3. Use `FakeAgentProviderFactory` for the migrated scenarios, adding an explicit semantic echo/reply operation through
   its existing private control channel where a scenario needs a provider response. Delete
   `EchoAgentProviderFactory` and its backend/runtime/session types once no scenario uses them.
4. Remove or merge bindings and setup helpers made unreachable by the migration. Do not duplicate scenarios already
   covered by the backend scenario, protocol-validation, transcript, lifecycle, or host-ownership features.
5. Run `StdioUiProtocol`, `StdioUiProtocolMultiRole`, `HeadlessUiClient`, and `HostCoexistence` independently, then run
   the related protocol-validation, transcript-synchronization, and host-ownership scenarios together.

**Slice acceptance:** Stdio framing, semantic headless-client behavior, and independent-host coexistence remain
observable through real published processes, with one shared process driver and no echo-provider or binding-owned
process harness.

### Slice 3: Sever product assembly references and remove dead harness artifacts

1. Replace the remaining direct product calls in test support: issue non-blocking shutdown by starting the published
   `squad-hq shutdown` command through the process driver, and create the Windows handoff-outbox junction through the
   test-owned command runner rather than `squad.Process.ProcessRunner`. Preserve the shutdown-admission,
   cleanup-ownership, and real handoff-pump failure scenarios.
2. Remove `ObservingHandoffPump` and every other unreferenced recording fake, fixture, helper, binding, empty feature,
   or generated orphan left by the white-box suite. Keep provider-selection descriptor fixtures only where a current
   process-level scenario consumes them, with one top-level C# type per file.
3. Remove every compile-time product `ProjectReference` from `squad.Specs` except
   `squad.AgentProvider.Abstractions`. Keep publishing `squad` and provider-free `squad-hq` as explicit MSBuild test
   preparation dependencies without exposing their assemblies to the specs compiler.
4. Scan step definitions and support code to ensure bindings neither import nor name product implementation types
   and that retained direct CLI/protocol bindings delegate only to the process driver, headless client, fake-agent
   controller, or semantic workspace observers.
5. Clean generated Reqnroll output, run the shutdown-admission, cleanup-diagnostics, handoff-pump-failure, delivery,
   host-control, provider-selection, and packaging scenarios independently, then run the complete `squad.Specs`
   suite.

**Slice acceptance:** `squad.Specs` compiles with only the provider-abstractions product reference, all executables are
exercised solely as published processes, no obsolete or orphaned white-box harness artifact remains, and every retained
scenario passes alone and in the complete suite.

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
