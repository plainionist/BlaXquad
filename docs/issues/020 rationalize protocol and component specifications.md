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

## Implementation plan

All retained scenarios run the published, provider-free `squad-hq --ui stdio` executable. Raw JSON framing stays
inside the test-owned headless UI client, provider observations stay behind the fake-agent controller, and Gherkin
describes only user, operator, provider, or wire-protocol outcomes. Existing process specifications are the
authoritative coverage when they already prove a contract; do not translate a direct assertion into a duplicate
scenario.

### Slice 1 [done]: Preserve UI protocol contracts through the published process

1. Replace `PhotinoUiProtocol.feature` with a technology-neutral protocol-validation feature driven through
   `BackendScenario` and the real stdio transport. Extend the headless UI support only with the raw-envelope input and
   typed protocol-error observations needed at this wire boundary.
2. Preserve the supported invalid-input matrix through JSON input/output: unsupported envelope version, missing or
   unknown type, missing role or request ID, invalid string, boolean, integer, or synchronization payload, and
   malformed JSON. Assert the documented `protocol.error`, absence of a provider-side command for rejected input, and
   continued usability of the same process after an error.
3. Keep command routing, interaction errors, readiness, state publication, synchronization, paging, and archived-entry
   behavior in the existing role-interaction, `StdioUiProtocol`, `PublishedInteractionsAndResponseOwnership`, and
   transcript features. Delete their recording-host duplicates rather than recreating them.
4. Delete the URL-open callback-order, send-callback-failure, and exact helper-call assertions because they have no
   transport-neutral JSON consequence. Remove `PhotinoUiProtocolSteps` and its recording/synchronization-context
   support when orphaned.
5. Delete `SnapshotPublication.feature` and `SnapshotPublicationSteps`: publisher delay, coalescing, disposal, and
   callback concurrency are implementation timing. Rely on `ActiveUsageRefresh`,
   `StdioUiProtocolMultiRole`, and `TranscriptSynchronizationOrder` for the supported guarantees that latest state is
   published and concurrent transcript traffic remains complete, ordered, and well framed.
6. Delete the remaining `ViewModel.feature` scenarios and direct bindings. Their state, interaction, transcript,
   multi-role isolation, startup, failure, and shutdown outcomes are already covered by the corresponding published
   process features; remove the recording application, host, provider, and lifecycle support made orphaned by this
   cleanup.

**Slice acceptance:** Protocol validation is exercised only through newline-delimited JSON sent to the real
`squad-hq` process, the process remains usable after rejected input, and no snapshot, protocol-session, ViewModel, or
application lifecycle component specification remains.

**Status: complete (10b6ff14b7).** `UiProtocolValidation.feature` drives the invalid-input matrix through the real
`squad-hq --ui stdio` process via raw newline-delimited JSON; the process stays usable after rejected input.
`PhotinoUiProtocol`, `SnapshotPublication`, and `ViewModel` features and their recording-host support are removed.
`51d67b9326` closes the review finding on the integer-payload error text: `JsonElement.TryGetInt32` throws
`InvalidOperationException` when `ValueKind` is not `Number`, so the asserted `protocol.error` matches production.

### Slice 2 [done]: Keep test providers behind the provider-neutral SPI

1. Replace direct `AgentEventChannel` use in the fake, echo, and controllable provider fixtures with one test-owned
   event-stream implementation over framework primitives. Keep event publication, completion, cancellation, and
   disposal private to the fixtures while continuing to expose only `IAgentSession.Events`.
2. Remove `AgentEventChannelTests.cs` and the direct `CopilotSdkAgentSession` tests. Channel depth, overflow-writer
   admission, timeout, and disposal mechanics are implementation details; retain provider-visible ordering and
   terminal-failure outcomes through the existing transcript-ordering and terminal-provider process scenarios.
3. Remove the fake provider's dependency on `CopilotToolOutputNormalizer`. Drive provider-neutral
   `AgentToolOutputChangedEvent` values through the control pipe, update affected tool-output scenarios and semantic
   step wording to describe complete provider output, and delete cases that exist only to infer Copilot SDK partial
   output semantics.
4. Remove the `squad.CopilotSdk` project reference and any other product references made unused by the deleted direct
   specifications. Keep provider packaging assertions at the published-artifact boundary.
5. Run the focused protocol, transcript, interaction, lifecycle, and provider-packaging scenarios, then scan the test
   project to confirm it neither constructs the prohibited production components nor references or loads
   `squad.CopilotSdk`.

**Slice acceptance:** The backend suite still proves ordered provider events, tool-output publication, session failure,
and clean shutdown through the process boundary, while no test constructs `AgentEventChannel` or
`CopilotSdkAgentSession` and the test assembly has no `squad.CopilotSdk` dependency.

**Status: complete (10f86944c5).** Fake, echo, and controllable fixtures publish through test-owned
`TestAgentEventStream` and expose only `IAgentSession.Events`. `AgentEventChannelTests.cs` and direct
`CopilotSdkAgentSession` tests are gone. The fake provider emits complete `AgentToolOutputChangedEvent` values
without `CopilotToolOutputNormalizer`; tool-output features describe that provider-neutral contract.
`squad.Specs` no longer references `squad.CopilotSdk`.

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
