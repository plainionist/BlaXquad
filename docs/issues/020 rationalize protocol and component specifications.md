---
title: Rationalize protocol and component specifications
priority: 20
---

# Rationalize protocol and component specifications

This issue implements the [backend test strategy](../manual/test-strategy.md).

## Goal

Review the remaining direct component specifications and either express their valuable contract through the real
process/protocol boundary or delete them.

The primary inputs are:

- `PhotinoUiProtocol.feature`;
- `SnapshotPublication.feature`;
- `AgentEventChannelTests.cs`; and
- any technical scenarios left behind by earlier migrations.

Do not move them to another test assembly and do not retain direct construction merely because the current test is
easy to run.

## Acceptance criteria

- The UI protocol feature is technology-neutral and runs through the headless `squad-hq` transport rather than
  constructing `UiProtocolSession`.
- Supported envelope-version, malformed-message, command-routing, error, transcript-request, and state-publication
  contracts remain covered through JSON input/output.
- Scenarios already covered by role-interaction or transcript features are deleted rather than duplicated.
- Snapshot coalescing or ordering is retained only where it produces an observable protocol guarantee; direct
  `SnapshotPublisher` disposal/timing tests are removed.
- `AgentEventChannelTests.cs` is removed unless its behavior can be stated and demonstrated as a provider-neutral,
  process-visible backend contract.
- No test directly constructs `SnapshotPublisher`, `TranscriptProtocol`, `UiProtocolSession`,
  `AgentEventChannel`, or `CopilotSdkAgentSession`.
- Exact callback-failure and helper-state assertions with no supported external consequence are deleted.
- Every remaining scenario has a clear user, operator, provider, or wire-protocol contract.
