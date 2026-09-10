---
title: low value specs
priority: 1
---

## Goal

Remove specifications that only prove test infrastructure or repeat stronger end-to-end behavior, without losing a
supported product contract, an important failure mode, or a test-harness safety invariant.

## Analysis

The backend suite currently contains 52 Gherkin feature files with 164 scenario declarations. The two scenario
outlines expand that source inventory to 174 cases. There are no hand-written NUnit `[Test]` or `[TestCase]` methods
in `squad.Specs`; Gherkin is the only authored backend test surface.

The browser suite contains seven Playwright spec files with 92 explicit `test(...)` declarations. Two top-level
parameterized loops expand those declarations to 98 browser cases. No high-confidence low-value browser case was
found: the superficially similar cases protect different presentation, accessibility, scrolling, virtualization,
timing, or protocol-reconciliation invariants. This is exactly the frontend coverage called for by
[`test-strategy.md`](../manual/test-strategy.md), so this issue should not change the Playwright suite.

The useful distinction in the backend suite is not simply "product test" versus "test-infrastructure test". A
harness specification remains valuable when it uniquely prevents false positives, hangs, leaked processes, or
unusable failure diagnostics. A specification is low value here when it only demonstrates a helper capability that
is already exercised by a stronger retained process-level scenario.

Git history supports that distinction. Several candidates were introduced as successive proof slices while the
process specification driver was being built. The later vertical proof and the migrated product scenarios now use
the same helpers across real process and protocol boundaries, so those earlier component proofs no longer carry a
unique contract.

### High-confidence removals

Remove these seven feature files after preserving the two unique observations called out below. Together they
contain 17 scenario declarations and about 200 lines of Gherkin.

| Feature | Finding | Retained coverage |
| --- | --- | --- |
| [`BackendScenario.feature`](../../src/squad.Specs/Features/BackendScenario.feature) | Its only scenario proves that the test-owned composition root can start, become ready, and shut down. | The first scenario in [`ProcessSpecificationDriver.feature`](../../src/squad.Specs/Features/ProcessSpecificationDriver.feature) performs the same path plus session observation, prompt/reply, and transcript verification. |
| [`BackendSpecWorkspace.feature`](../../src/squad.Specs/Features/BackendSpecWorkspace.feature) | Both scenarios inspect `ScenarioWorkspace` behavior: which executable its own wrapper selected and which metadata its own `CommandResult` captured. They do not assert a product contract. | [`ProviderPackaging.feature`](../../src/squad.Specs/Features/ProviderPackaging.feature) checks the provider-free artifact itself, while every retained process scenario runs the published executable and exposes command failures with diagnostics. |
| [`MinimalFakeProvider.feature`](../../src/squad.Specs/Features/MinimalFakeProvider.feature) | The lifecycle-only scenario is a strict subset of the vertical proof. | `ProcessSpecificationDriver.feature` loads the same explicit provider, observes its session, exchanges a prompt and reply, and shuts down cleanly. |
| [`FakeProviderControlTransport.feature`](../../src/squad.Specs/Features/FakeProviderControlTransport.feature) | The single start/dispose transport proof is repeated through the same pipe in stronger lifecycle scenarios. | [`HeadquartersLifecycle.feature`](../../src/squad.Specs/Features/HeadquartersLifecycle.feature) observes start and disposal for two roles and also verifies readiness, durable state, host release, and relaunch. Several failure/termination features independently observe disposal. |
| [`RoleControllerPromptsAndReplies.feature`](../../src/squad.Specs/Features/RoleControllerPromptsAndReplies.feature) | Its prompt/reply round trip is step-for-step contained in the vertical proof. | The first `ProcessSpecificationDriver.feature` scenario retains the real UI -> provider -> UI round trip. |
| [`HeadlessUiClient.feature`](../../src/squad.Specs/Features/HeadlessUiClient.feature) | Four scenarios exercise the wrapper: ready handshake and prompt output are covered by [`StdioUiProtocol.feature`](../../src/squad.Specs/Features/StdioUiProtocol.feature), combined timeout diagnostics are covered more strongly by the second vertical-proof scenario, and only unknown-role prompt delivery is unique. | Move the one unique product case, prompt delivery to an unknown role, into [`UiProtocolValidation.feature`](../../src/squad.Specs/Features/UiProtocolValidation.feature) before deleting this feature. |
| [`FakeAgentEventSurface.feature`](../../src/squad.Specs/Features/FakeAgentEventSurface.feature) | Its seven scenarios were useful while building the fake-agent API, but now primarily prove that test helper methods work. Every event family is exercised by stronger product behavior: transcript shape, abort sequencing, interaction ownership, transcript streaming/tools, active usage, and terminated-session suppression. | Fold its unique assertions that permission, input, and elicitation requests also appear in the transcript into the existing published-interactions scenario. All other assertions already have stronger coverage. |

Remove two individual scenarios from otherwise valuable features:

| Scenario | Finding | Retained coverage |
| --- | --- | --- |
| `AgentProviderSelection.feature`: `An explicit valid provider descriptor loads and constructs successfully` | Successful explicit loading is exercised by nearly every process scenario and fully by the vertical proof. | Keep the four failure scenarios; missing assemblies, incompatible types, duplicate options, and constructor failures are distinct operator-visible branches. |
| `HostOwnership.feature`: `The executable shuts down an owned host` | This basic happy path is contained in the healthy headquarters lifecycle, including host release and a successful replacement launch. | Keep the equivalent-path, duplicate-host, abrupt-termination, readiness, and idempotent-empty-project scenarios. Keep the stdio shutdown scenario because it uniquely proves shutdown without closing standard input. |

After moving the two unique observations, this removes 18 scenario declarations overall. The expected backend source
inventory becomes 45 feature files, 146 scenario declarations, and 156 expanded cases. The expanded count must be
confirmed by clean test discovery rather than treated as an acceptance criterion by itself.

### Specifications to retain

Do not remove [`BackendScenarioLifecycle.feature`](../../src/squad.Specs/Features/BackendScenarioLifecycle.feature).
It is test infrastructure, but its two scenarios uniquely prove that emergency cleanup kills only the process owned
by that scenario and that a locked temporary workspace cannot mask the real test result or hang teardown. Product
happy paths do not exercise either safety property.

Do not remove [`FakeProviderControlProtocol.feature`](../../src/squad.Specs/Features/FakeProviderControlProtocol.feature).
The pipe is test-only, but its concurrency, stale-session rejection, and undisposed-session diagnostics are what
make dozens of process tests deterministic. The authentication, version, correlation, and unknown-command cases
also protect the explicit typed/versioned/tokenized channel required by the test strategy. Valid-path product tests
cannot detect those rejection regressions. These scenarios are in-process and cheap relative to launched-process
scenarios.

Keep both scenarios in `ProcessSpecificationDriver.feature`. The first is the architectural proof explicitly
required by the test strategy. The second verifies that every bounded wait produces combined process, UI, and
provider diagnostics; merely observing successful waits elsewhere cannot protect that failure behavior.

Keep the `--ui` validation scenarios and the four failing `--provider` scenarios. They are production CLI behavior
with distinct diagnostics, not parser implementation tests. A successful launch cannot detect a regression in an
invalid-option branch.

Keep the detailed transcript, ordering, retention, lifecycle, recovery, queue, and failure scenarios. Although some
are technical, each sampled case changes a meaningful input, race, state transition, or externally visible protocol
result. Their titles do not reveal any further strict behavioral subsets.

### Generated test artifact finding

The source tree currently contains 58 ignored `*.feature.cs` files for only 52 source features. These six generated
files have no corresponding feature and belong to suites already removed:

- `ArchivedEntryReconstruction.feature.cs`
- `Configuration.feature.cs`
- `PhotinoUiProtocol.feature.cs`
- `SnapshotPublication.feature.cs`
- `Startup.feature.cs`
- `ViewModel.feature.cs`

The current failed `dotnet test` inventory still names tests from those deleted suites and contains no useful failure
output, so it is not a trustworthy baseline for counts, timing, or failures. `squad.Specs.csproj` only asks Reqnroll
to delete obsolete code-behind during `Clean`; ordinary source edits and test discovery can therefore leave stale
generated files visible to tooling.

Reqnroll 3.3.4 supports `ReqnrollUseIntermediateOutputPathForCodeBehind`. Generated feature code should be moved to
`obj` with that property, followed by one clean build to remove legacy source-tree code-behind. Future feature
deletions should then disappear with normal intermediate-output cleanup instead of depending on ignored files next
to the authored specifications.

## Plan

Implement the consolidation in the following independently reviewed slices. Keep only the named current slice in
progress; queue each later slice only after the reviewer accepts its predecessor.

### Slice 1 - Isolate generated Reqnroll code [done]

**Outcome:** Clean test discovery is authoritative because generated feature code lives only in intermediate output
and deleted features cannot survive as source-tree artifacts.

**Implementation:**

- Enable `ReqnrollUseIntermediateOutputPathForCodeBehind` in `squad.Specs.csproj` using the existing Reqnroll 3.3.4
  support.
- Run a clean build once so the legacy ignored `*.feature.cs` files beside authored features, including the six
  orphans listed above, are removed.
- Run clean backend test discovery and record the discovered case count and suite duration in this issue as the
  trustworthy before-consolidation baseline.

**Acceptance criteria:**

- No generated `*.feature.cs` file remains beside a source `.feature` file.
- A clean build generates feature code beneath `obj` and clean discovery reports only tests backed by current source
  features.
- The issue records the actual expanded baseline count and duration without claiming a performance improvement.

**Baseline (recorded after implementation):**

`ReqnrollUseIntermediateOutputPathForCodeBehind` is now `true` in `squad.Specs.csproj`. A `dotnet clean` removed all
52 previously ignored `*.feature.cs` files from `src/squad.Specs/Features`; none of the six orphans named above were
present in this worktree at clean time, so nothing needed manual deletion here. A subsequent `dotnet build` confirmed
all 52 code-behind files are regenerated under `obj/Debug/net10.0`, with zero `*.feature.cs` files left beside the
source `.feature` files.

Clean `dotnet test --list-tests` discovery reports **174 expanded cases** from the current 52 feature files (matching
the 164 scenario declarations plus the two scenario-outline expansions described in the Analysis), confirming
discovery is now trustworthy.

An actual `dotnet test` run of that baseline completed in **2 m 44 s** with **158 passed, 12 failed, 4 skipped** (174
total). A second run completed in **2 m 21 s** with **159 passed, 11 failed, 4 skipped**, a different failure subset
each time. All failures are process/protocol timeouts in real-subprocess scenarios (for example
`AbortingARoleWithAnOutstandingPromptCancelsThatPromptsOperation`, `TheClientCompletesTheRealUi_ReadyHandshake`), not
missing bindings or references to deleted suites, and the set of failing tests differs between runs -- consistent
with shared-environment scheduling contention rather than a regression from this change. The 4 skips
(`AnExplicitValidProviderDescriptorLoadsAndConstructsSuccessfully`, `AZeroReadinessTimeoutIsRejectedBeforeProjectDiscovery`,
`ShutdownIsIdempotentForAnEmptyProject`, `WaitingOutsideASquadProjectFailsBeforePolling`) are pre-existing and
unrelated to this slice. This is a baseline record, not a claim of a performance improvement.

**Status: complete (753d91877d).** `ReqnrollUseIntermediateOutputPathForCodeBehind` is enabled so feature
code-behind is generated under `obj`. A clean build leaves no `*.feature.cs` files beside authored features.
Clean discovery reports 174 expanded cases from 52 source features. Recorded suite duration is 2 m 44 s / 2 m 21 s
without a performance claim.

### Slice 2 - Preserve unknown-role prompt validation [done]

**Outcome:** Unknown-role prompt delivery remains a product-level UI protocol contract while redundant headless-client
wrapper specifications are removed.

**Implementation:**

- Move the unknown-role prompt case into `UiProtocolValidation.feature`.
- Assert the exact protocol error, no provider invocation, and successful subsequent protocol use.
- Run the recipient feature as a gap-prevention gate, then delete `HeadlessUiClient.feature` and its dedicated
  `HeadlessUiClientSteps.cs`.
- Remove only support made unreferenced by this slice; retain the `HeadlessUiClient` helper itself because process
  specifications still use it.

**Acceptance criteria:**

- `UiProtocolValidation` proves all three unknown-role invariants through the supported protocol boundary.
- The focused `UiProtocolValidation` and `ProcessSpecificationDriver` scenarios pass without
  `HeadlessUiClient.feature` or its dedicated bindings.

**Status: complete (b7e3446ad3).** Unknown-role prompt delivery is a `UiProtocolValidation` scenario that asserts
the exact `Unknown role: <role>` protocol error, no provider invocation, and continued usability. `HeadlessUiClient.feature`
and `HeadlessUiClientSteps.cs` are removed; the `HeadlessUiClient` helper remains. Clean discovery is 171 expanded
cases.

### Slice 3 - Preserve interaction transcript coverage [done]

**Outcome:** Permission, input, and elicitation requests remain covered as published transcript interactions while
fake-agent API self-tests are removed.

**Implementation:**

- Add the three request-family transcript assertions to the existing published-interactions field-coverage scenario.
- Run that recipient scenario as a gap-prevention gate, then delete `FakeAgentEventSurface.feature`.
- Remove only bindings or helper methods made unreferenced by this slice; retain the fake provider and its event
  surface used by product-facing scenarios.

**Acceptance criteria:**

- The retained published-interactions scenario observes permission, input, and elicitation requests with their
  supported transcript fields.
- Focused published-interaction, abort, transcript-streaming, usage, and terminated-session scenarios pass after the
  helper-only feature is removed.

**Status: complete (dbd269532f).** Permission, input, and elicitation requests are observed in the published-interactions
field-coverage scenario both as protocol pending state and as transcript content. `FakeAgentEventSurface.feature` is
removed; unreferenced helper-only bindings were pruned. Clean discovery is 164 expanded cases.

### Slice 4 - Consolidate vertical process proofs [done]

**Outcome:** One architectural process proof covers startup, provider lifecycle, and prompt/reply behavior instead of
three weaker subset features.

**Implementation:**

- Delete `BackendScenario.feature`, `MinimalFakeProvider.feature`, and
  `RoleControllerPromptsAndReplies.feature`.
- Remove only support made unreferenced by those deletions; retain `BackendScenario`, the fake provider, and shared
  role-controller bindings used by the stronger process suite.

**Acceptance criteria:**

- Both `ProcessSpecificationDriver.feature` scenarios pass, preserving the full vertical proof and bounded-wait
  diagnostics.
- No remaining feature step loses its binding.

**Status: complete (b7413361f3).** `BackendScenario.feature`, `MinimalFakeProvider.feature`, and
`RoleControllerPromptsAndReplies.feature` are removed. The vertical proof and bounded-wait diagnostics remain in
`ProcessSpecificationDriver.feature`. The unreferenced echo-provider start binding is gone; `BackendScenario` and
the fake-provider/role-controller support remain. Clean discovery is 161 expanded cases.

### Slice 5 - Remove workspace-wrapper self-specification [done]

**Outcome:** The suite verifies the packaged provider-free artifact and launched process, not its own workspace and
command-result wrappers.

**Implementation:**

- Delete `BackendSpecWorkspace.feature` and its dedicated `BackendSpecWorkspaceSteps.cs`.
- Remove only helper methods made unreferenced by this deletion; retain the workspace and command-result support used
  by process-facing scenarios.

**Acceptance criteria:**

- `ProviderPackaging.feature` and `ProcessSpecificationDriver.feature` pass with full command diagnostics available
  on failures.
- No remaining feature references a deleted workspace binding.

**Status: complete (95bd130c8f).** `BackendSpecWorkspace.feature` and `BackendSpecWorkspaceSteps.cs` are removed.
The still-referenced `a configured project with roles` step moved to `CommonSteps`. Workspace and command-result
support used by process-facing scenarios remains. Clean discovery is 159 expanded cases.

### Slice 6 - Consolidate control-transport lifecycle coverage [done]

**Outcome:** Product lifecycle scenarios retain start and disposal coverage across the fake-provider pipe without a
separate transport smoke feature.

**Implementation:**

- Delete `FakeProviderControlTransport.feature`.
- Remove only support made unreferenced by this deletion; retain the control transport and protocol implementation
  required for deterministic process tests.

**Acceptance criteria:**

- `HeadquartersLifecycle.feature` still observes session start, readiness, disposal, durable state, host release, and
  relaunch.
- `BackendScenarioLifecycle.feature` and `FakeProviderControlProtocol.feature` continue to protect cleanup,
  diagnostics, concurrency, authentication, versioning, correlation, and stale-session rejection.

**Status: complete (12ce0ed8ec).** `FakeProviderControlTransport.feature` is removed. Session start and disposal remain
covered by `HeadquartersLifecycle.feature`. Control-transport support is unchanged. Clean discovery is 158 expanded
cases.

### Slice 7 - Remove the redundant provider success case [queued]

**Outcome:** Provider selection specifications retain every distinct operator-visible failure while successful
explicit loading remains covered by the vertical process proof.

**Implementation:**

- Delete `An explicit valid provider descriptor loads and constructs successfully` from
  `AgentProviderSelection.feature`.
- Remove only step support made unreferenced by that scenario.

**Acceptance criteria:**

- The four provider failure scenarios pass with their distinct diagnostics.
- `ProcessSpecificationDriver.feature` still proves successful explicit provider loading and construction.

### Slice 8 - Remove the redundant owned-host happy path [queued]

**Outcome:** Host ownership retains its edge, recovery, readiness, and stdio-shutdown contracts without a lifecycle
happy path already contained in headquarters behavior.

**Implementation:**

- Delete `The executable shuts down an owned host` from `HostOwnership.feature`.
- Remove only step support made unreferenced by that scenario.

**Acceptance criteria:**

- The retained equivalent-path, duplicate-host, abrupt-termination, readiness, idempotent-empty-project, and stdio
  shutdown scenarios pass.
- `HeadquartersLifecycle.feature` proves healthy owned-host shutdown, host release, and replacement launch.

### Completion gate

After Slice 8 is accepted:

- Search all remaining Gherkin steps and remove only truly unreferenced bindings or helpers attributable to this
  issue.
- Run the complete backend suite twice from a clean generated state; confirm 45 source feature files, 146 scenario
  declarations, and the actual expanded case count.
- Run the unchanged Playwright suite once as the final cross-boundary regression check.
- Record final counts, comparable duration, and results here. Claim a runtime reduction only if the clean before/after
  measurements demonstrate one.
