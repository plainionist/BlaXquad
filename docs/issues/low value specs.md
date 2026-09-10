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
| [`HeadlessUiClient.feature`](../../src/squad.Specs/Features/HeadlessUiClient.feature) | Three scenarios re-prove the wrapper: ready handshake and prompt output are covered by [`StdioUiProtocol.feature`](../../src/squad.Specs/Features/StdioUiProtocol.feature), and combined timeout diagnostics are covered more strongly by the second vertical-proof scenario. | Move the one unique product case, prompt delivery to an unknown role, into [`UiProtocolValidation.feature`](../../src/squad.Specs/Features/UiProtocolValidation.feature) before deleting this feature. |
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

1. Establish a reliable baseline. Configure Reqnroll to generate code-behind under `obj`, run a clean build once,
	verify that no `*.feature.cs` files remain beside source features, and run clean test discovery. Record the actual
	expanded case count and suite duration.
2. Preserve the two unique product observations. Add an unknown-role prompt scenario to `UiProtocolValidation.feature`
	that asserts the exact protocol error, no provider invocation, and continued usability. Add transcript assertions
	for permission, input, and elicitation requests to the existing published-interactions field-coverage scenario.
3. Run the two recipient features before deleting anything. This is the gap-prevention gate for the consolidation.
4. Delete the seven high-confidence feature files listed above. Delete their dedicated binding classes
	`BackendSpecWorkspaceSteps.cs` and `HeadlessUiClientSteps.cs`; the other candidate features use shared bindings.
5. Delete the one redundant successful-provider scenario and the one redundant basic-shutdown scenario from their
	otherwise retained features.
6. Search every remaining Gherkin step before pruning support. Remove only bindings and helper methods with no
	remaining feature reference; retain `BackendScenario`, `HeadlessUiClient`, the fake provider, and the control
	protocol because the product-facing suite depends on them extensively.
7. Run focused acceptance tests for `UiProtocolValidation`, `PublishedInteractionsAndResponseOwnership`,
	`ProcessSpecificationDriver`, `HeadquartersLifecycle`, `BackendScenarioLifecycle`,
	`FakeProviderControlProtocol`, `AgentProviderSelection`, and `HostOwnership`.
8. Run the complete backend suite from a clean generated state twice. The second run is a cheap check that removing
	infrastructure smoke scenarios did not expose ordering, teardown, or stale-artifact flakiness. Confirm the target
	source inventory of 45 features and 146 scenario declarations.
9. Run the unchanged Playwright suite once as the final cross-boundary regression check. No browser spec removal is
	planned by this issue.
10. Update this issue with final before/after case counts, duration, and test results. Do not claim a runtime reduction
	 until those measurements are taken from clean, comparable runs.

