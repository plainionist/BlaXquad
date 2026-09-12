---
title: fight primitive obsession
priority: 1
---

do we still have dictionaries with string as key? 
what is that string? can we use one of the domain value objects like roleId etc?
should we introduce some enum?

strings in general - not only dictionary but also parameters and return values - which should be converted to enums in the heart of the app?

code needs to be explicit!

not only check product code also the specs assembly

if we keep strings this needs to be justified!
example: it probably makes sense to keep strings in the protocol to the UI

## Current-state audit (2026-09-12)

The relevant distinction is not "string bad". A string is appropriate while it represents text or an external
encoding. It needs replacing when application code already knows that it represents one closed choice or one
specific kind of identity. Parse at an input boundary, keep the typed value through the owning code, and format it
again only at an output boundary.

### Remaining cases

#### Permission mode

`permissions` is the closed set `prompt | approveAll`, but
[`AgentSettings`](../../src/squad.Domain/AgentSettings.cs) and
[`SquadAgentConfiguration`](../../src/squad.Configuration/SquadAgentConfiguration.cs) retain it as `string` after
configuration validation. [`CopilotSdkClient`](../../src/squad.AgentProvider.CopilotSdk/CopilotSdkClient.cs) then
branches on the protocol spelling `"approveAll"`.

- Introduce a `PermissionMode` enum next to `ReceiveMode`.
- Parse the JSON spelling in `SquadConfigurationLoader`, carry the enum through the domain and provider context,
	and map it to provider behavior in the Copilot adapter.
- Keep `SquadConfigurationAgentDocument.Permissions` as `string?`: it is the JSON input DTO and must also represent
	unsupported values so validation can report them.

#### Validated configuration

The document DTOs correctly retain raw strings, but the successful result of
[`SquadConfigurationLoader`](../../src/squad.Configuration/SquadConfigurationLoader.cs) does too:

- [`SquadConfiguration`](../../src/squad.Configuration/SquadConfiguration.cs) exposes role definitions and the
	leader as strings.
- [`SquadMemberConfiguration`](../../src/squad.Configuration/SquadMemberConfiguration.cs) exposes the member name,
	reusable role, and worktree selector as strings.
- [`SquadMemberDefinition`](../../src/squad.Domain/SquadMemberDefinition.cs) makes `ReceiveMode` nullable so the
	lenient command-side reader can represent an invalid raw token on a shared domain type.

The validated model should expose `IReadOnlyList<RoleId>`, `SquadMemberId Leader`, and member properties typed as
`SquadMemberId` and `RoleId`. A configuration-owned worktree target should distinguish the project root from a
named linked worktree instead of carrying the sentinel `"master"` into
[`WorkspacePreparer`](../../src/squad.Workspaces/WorkspacePreparer.cs) and command-side path construction.

`SquadMemberDefinition.ReceiveMode` should be non-null. The lenient
[`SquadConfig`](../../src/squad.Configuration/SquadConfig.cs) path exists to preserve command-specific diagnostics
for malformed configuration; its parse failure belongs in a configuration result, not in the domain definition as
`null`. Raw document fields must stay strings so unsupported values can still be diagnosed.

#### Configured member identity

`SquadMemberId` is the runtime identity of one configured participant. `RoleId` identifies a reusable prompt/role
definition. Many older APIs still call a member identity `role` and unwrap it to `string`, which makes these two
concepts interchangeable again.

The member identity should remain typed through these paths:

- [`AgentRoleContext`](../../src/squad.AgentProvider.Abstractions/AgentRoleContext.cs) and
	[`IAgentSession.Role`](../../src/squad.AgentProvider.Abstractions/IAgentSession.cs).
- Public and private member operations in
	[`SquadMembers`](../../src/squad.Application/SquadMembers.cs). Its authoritative `myMembers` dictionary is already
	correctly keyed by `SquadMemberId`; callers should not repeatedly wrap string parameters at lookup time.
- [`MemberTranscriptState`](../../src/squad.Application/Transcripts/MemberTranscriptState.cs),
	[`MemberTranscriptArchive`](../../src/squad.Application/Transcripts/MemberTranscriptArchive.cs),
	[`GenerationTranscriptArchive`](../../src/squad.Application/Transcripts/GenerationTranscriptArchive.cs), and the
	per-member sets/maps in [`TranscriptArchive`](../../src/squad.Application/Transcripts/TranscriptArchive.cs).
- [`ISquadUi`](../../src/squad.Ui.Abstractions/ISquadUi.cs),
	[`ITranscriptUi`](../../src/squad.Ui.Abstractions/ITranscriptUi.cs), and their snapshot, page, update, and archived
	entry records. [`UiCommandHandler`](../../src/squad.Ui.Protocol/UiCommandHandler.cs) should parse the incoming wire
	string once; [`TranscriptProtocol`](../../src/squad.Ui.Protocol/TranscriptProtocol.cs) should format the typed ID
	when serializing.
- Transcript synchronization maps and journals in
	[`UiDeliveryCoordinator`](../../src/squad.Ui.Protocol/UiDeliveryCoordinator.cs) and
	[`TranscriptAnnouncementJournal`](../../src/squad.Ui.Protocol/TranscriptAnnouncementJournal.cs).
- Sender, recipient, and delivery lookup state in
	[`HandoffDocument`](../../src/squad.Handoffs/HandoffDocument.cs) and
	[`HandoffDeliveryService`](../../src/squad.Handoffs/Delivery/HandoffDeliveryService.cs).

Names on internal APIs and records should change from `Role` to `MemberId` where that is what they contain. This is
not merely cosmetic: configurations may assign the same `RoleId` to several independently addressable members.
The unused string `MemberSnapshot.Role` in
[`MemberSnapshot`](../../src/squad.Application/Members/MemberSnapshot.cs) should be removed rather than wrapped.

#### Interaction ownership and identifiers

[`AgentPermissionRequest`](../../src/squad.AgentProvider.Abstractions/Agents/AgentPermissionRequest.cs),
[`AgentInputRequest`](../../src/squad.AgentProvider.Abstractions/Agents/AgentInputRequest.cs), and
[`AgentElicitationRequest`](../../src/squad.AgentProvider.Abstractions/Agents/AgentElicitationRequest.cs) duplicate a
string `Role` on every event. The event channel and owning session already determine the member. Remove that field
rather than adding another identity copy that can disagree with its route.

The request IDs on those events, their responses, UI contracts, and the pending-interaction dictionaries in
[`MemberAggregate`](../../src/squad.Application/Members/MemberAggregate.cs) should use an opaque
`InteractionRequestId` value object. Similarly, tool event IDs and transcript correlation state should use a
`ToolCallId` value object. These values need no invented format validation; the types prevent request IDs, tool call
IDs, session IDs, and arbitrary text from being mixed. Fake-provider and UI JSON still serialize them as strings.

`IAgentSession.SessionId` may remain a string. It is an opaque provider/control identifier used as supplied and is
not used as a domain key or confused with the two application-owned ID kinds above.

#### Elicitation choices

The request `Mode` and [`AgentElicitationResponse.Action`](../../src/squad.AgentProvider.Abstractions/AgentElicitationResponse.cs)
are closed provider-neutral choices carried as strings. Introduce `ElicitationMode` and `ElicitationAction` enums;
the confirmed action values are `Accept`, `Decline`, and `Cancel`. Provider adapters and `UiCommandHandler` should
map their SDK/wire spellings at the boundary. Accepted form content remains `JsonElement?` because its schema is
provider/request-defined rather than a closed application type.

#### Tool read capability

[`AgentToolStartedEvent.Kind`](../../src/squad.AgentProvider.Abstractions/Agents/AgentToolStartedEvent.cs) is an
optional string whose only application meaning is the comparison to `"read"` in
[`MemberEventProjector`](../../src/squad.Application/Members/MemberEventProjector.cs). Normalize that in the provider
adapter and expose an explicit provider-neutral read capability on the event. Keep tool names as strings: names are
an open provider/plugin vocabulary, and the known-name sets are compatibility classification tables rather than a
closed domain enum.

#### Transcript source

[`TranscriptEntry.Source`](../../src/squad.Ui.Abstractions/TranscriptEntry.cs) is populated from a closed set of
string literals in `MemberEventProjector`, persisted in transcript archives, and interpreted by the UI. Replace it
with a `TranscriptSource` enum in the internal/application-facing contracts. Archive and UI protocol serializers
must preserve the existing stable lowercase spellings.

#### Handoff values and storage layout

[`HandoffDocument`](../../src/squad.Handoffs/HandoffDocument.cs) still represents application-owned IDs, bounded
priority, and lifecycle instants as primitives:

- `Id` should be a `HandoffId`.
- `From`, `To`, and `Recipient` should use `SquadMemberId`.
- `Priority` should be a `HandoffPriority` that owns the `00..99` invariant and two-digit CLI/filename formatting.
- `CreatedAt`, `EnqueuedAt`, `DequeuedAt`, and `CompletedAt` should be `DateTimeOffset` values.
- The canonical commit produced after Git revision resolution should use a `GitCommitId`; the raw user-supplied
	revision remains a string at the Git/CLI boundary.

The JSON schema and queue filenames are compatibility boundaries, so converters/path formatting must preserve
their current strings and numeric JSON representation. Task names and note messages remain strings because they are
human-authored text; their existing required/length rules still need validation when documents are read, not only
when the CLI creates them.

Queue bucket names and relative paths (`outbox`, `sent`, `failed`, `inbox/new`, `inbox/in_process`, and
`inbox/completed`) are repeated across commands, workspaces, delivery, and specs. Centralize this vocabulary in a
handoff queue layout/path API. The resulting filesystem paths remain strings because that is the platform API, but
callers should request a semantic bucket instead of assembling one from literals.

#### Boundary choices leaking inward

- [`Handoff`](../../src/squad/Commands/Handoff.cs) validates the command intent as `commit | note` and then keeps
	branching on the string. Map it once to the existing `HandoffKind`.
- [`HeadquartersControlClient`](../../src/squad.Runtime/Control/HeadquartersControlClient.cs) returns status strings
	from `QueryAgentStatusAsync` and branches on `ready | unknown-role | not-ready`. Parse the control response to a
	local readiness result/enum before the polling logic sees it. The JSON command and response tokens remain strings
	at the control-protocol edge.
- [`SessionGeneration`](../../src/squad.Runtime/SessionGeneration.cs) recognizes normal shutdown by comparing an
	`InvalidOperationException.Message` to `"Squad is shutting down"`. Return an explicit command-admission/lifecycle
	result (or use the existing cancellation signal) instead. Prose must not be a control-flow discriminator, and a
	new custom exception is unnecessary.

#### Specification state

Gherkin parameters begin as strings by design. Once a configured member has been accepted, however, fixture state
should use `SquadMemberId`. Current examples include:

- `BackendScenario.myConfiguredRoles` and the member-to-worktree map in
	[`ScenarioWorkspace`](../../src/squad.Specs/Support/Scenarios/ScenarioWorkspace.cs).
- Per-member transcript paging, delta, and sequence dictionaries in
	[`BackendScenarioSteps`](../../src/squad.Specs/StepDefinitions/BackendScenarioSteps.cs).
- Readiness waits in
	[`HeadquartersLifecycleSteps`](../../src/squad.Specs/StepDefinitions/HeadquartersLifecycleSteps.cs) and transcript
	synchronization state in
	[`StdioUiProtocolSteps`](../../src/squad.Specs/StepDefinitions/StdioUiProtocolSteps.cs).
- Member-keyed lifecycle, prompt, and observation state in the fake provider's
	[`ObservationJournal`](../../src/squad.AgentProvider.Fake/Control/ObservationJournal.cs), after its control JSON has
	been parsed.
- Parsed semantic handoff observations in
	[`QueuedHandoff`](../../src/squad.Specs/Support/Mailboxes/QueuedHandoff.cs), while keeping the observer independent
	of the production serializer.

Closed fixture state should also be typed locally. For example,
`BackendScenarioSteps.myLastInvalidMessageCase` is later switched as a category; store the expected provider effect
as a small test enum instead of retaining a free-form scenario label.

Do not type raw configuration builders, malformed JSON, Gherkin table cells, protocol envelopes, or exact wire
observations before the system under test sees them. Those strings are required to specify unsupported values and
prove the external representation.

### Strings intentionally retained

| Category | Examples | Why it remains text |
| --- | --- | --- |
| Raw configuration | `SquadConfigurationDocument`, member/agent document DTOs, raw receive mode used for diagnostics | Input DTOs must represent missing, empty, and unsupported values before validation. |
| UI and control wire formats | `UiMessage`, serialized envelope `type`/`role`/`requestId`, Vue protocol interfaces, Headquarters control JSON | These are stable external encodings. Parse at dispatch and format at publication; do not expose C# enum member names as the protocol. |
| Fake-provider control protocol | Command/event kinds, raw IDs, and `JsonElement` payloads | This is a cross-process test protocol and must inject malformed or future values. Any enums belong locally to the fixture after parsing, not in `squad.Domain`. |
| Gherkin and black-box observations | Step parameters, table headers/cells, raw JSON builders, stdout/status/operation assertions | Specs must express invalid input and assert exact public text without importing production serialization behavior. |
| Frontend presentation state | Member-keyed Vue maps and lowercase status/source strings received from the protocol | Vue owns transient presentation and protocol reconciliation, not authoritative member identity or domain validation. TypeScript string unions may document the wire vocabulary without duplicating C# rules. |
| Human-authored text | Prompts, transcript content, reasoning, errors, display names, task names, note messages | These values are open text, not identities or closed choices. Validate bounded fields without inventing enums. |
| Provider/plugin vocabulary | Model, effort, tool, skill and agent names; provider/hosting assembly and type descriptors | These sets are external, extensible, or selected by plug-ins. A core enum would reject valid future values. |
| Platform values | Filesystem paths, environment variables, process arguments/output, Git revision input and command output | The operating system, process, and Git APIs are string boundaries. Domain IDs should be formatted only when entering them. |
| CLI syntax | Option names, command-line arguments, and parser option dictionaries | Tokens are strings while parsing. Closed choices should become typed immediately after successful parsing. |
| Opaque external IDs | Provider session IDs and UI request correlation IDs that are only echoed | No application invariant or cross-kind operation is performed on them; wrapping every pass-through identifier would add ceremony without preventing a current mistake. |
| Token tables | Known tool names, shared assembly names, supported Gherkin columns, and JSON property names | These sets classify open external names or validate an external shape; they are not collections of domain entities. |
| Test context slots | `ScenarioWorkspace.myValues` keys | This is a heterogeneous fixture property bag addressed by private constant keys, not member identity. Prefer dedicated typed fixture state when a value gains behavior, but no domain ID or enum describes the keys. |

### Already fixed

- `RoleId` now identifies reusable role definitions and `SquadMemberId` identifies configured participants.
- `SquadMembers.myMembers` is keyed by `SquadMemberId`, so the authoritative runtime collection cannot confuse role
	definitions with members.
- `ReceiveMode`, `SquadMemberStatus`, and `HandoffKind` are enums in owning C# code. Their JSON, CLI, and UI
	spellings are mapped explicitly at boundaries.

These cases should stay fixed while addressing the remaining call sites; replacing a typed value with its `.Value`
before a boundary would recreate the same primitive obsession under a different name.

## execution hint

@architect: every new type introduced should be done with its own slice to make review simple and effective

## Implementation plan

### Execution rules

- Execute the slices below strictly in order and keep exactly one slice in progress. A later slice starts only after
  the previous slice has been accepted by the reviewer.
- A slice marked **New type** may introduce exactly the one named type. It must not add another top-level, nested,
  generated, helper, DTO, converter, or test type. A slice marked **New types: none** must introduce no type. If an
  additional type becomes necessary, stop and return the slice to the architect so the plan can be split first.
- Put every new top-level C# type in its own source file. Do not add default interface implementations or a custom
  exception type.
- Keep raw JSON/configuration fields, CLI arguments, Gherkin parameters, provider/control envelopes, and Vue
  protocol fields as strings. Parse once when they enter typed C# code and format once when they leave it.
- Preserve all existing public spellings, diagnostics, JSON shapes and scalar kinds, queue filenames, transcript
  ordering, and lifecycle behavior. In particular, typed enums must never leak their C# names or numeric values to
  JSON.
- Each slice includes all production call sites, directly coupled test support, focused black-box acceptance
  coverage, and directly affected manual text needed to leave that intermediate commit buildable and accurate.

### Slice 1 - Separate lenient command configuration [done]

**Task:** `separate-lenient-member-configuration`

**New type:** `SquadConfigMember` in `squad.Configuration`.

Replace the command-side reuse of `SquadMemberDefinition` with a narrow configuration result containing only the
typed member ID, resolved worktree path, parsed nullable receive mode, and raw receive-mode token needed for the
existing command diagnostics. Change `SquadConfig.ReadMembers`, `CurrentRoleResolver`, `context`, `handoff`,
`ready-for-next`, and `done-with-current` to consume that result. Remove the second raw-configuration scan.

Once malformed command input is isolated in `SquadConfigMember`, make `SquadMemberDefinition.ReceiveMode`
non-nullable and update its documentation. The validated launch path must never construct an invalid domain
definition, while the lenient command path must still distinguish:

- an omitted mode, which defaults to `ReceiveMode.Task`;
- an explicitly empty mode, which keeps the existing `Unknown role` result;
- an unsupported token, which keeps `INVALID_RECEIVE_MODE`;
- malformed or absent configuration, which keeps the existing command failure behavior.

**Acceptance:** `TaskQueue.feature`, `BatchQueue.feature`, `Context.feature`, and `Handoffs.feature` retain their
observable results; the domain model contains no nullable `ReceiveMode`. No type other than `SquadConfigMember` is
added.

**Status: complete (72d80491be).** `SquadConfigMember` is the lenient command-side read result (typed member ID,
resolved worktree path, nullable parsed receive mode, and raw token). `SquadConfig.ReadMembers`, `CurrentRoleResolver`,
and the context/handoff/ready-for-next/done-with-current commands consume it. The second raw-configuration scan is
gone. `SquadMemberDefinition.ReceiveMode` is non-nullable.

### Slice 2 - Type validated configuration identities [done]

**Task:** `type-validated-configuration-identities`

**New types:** none.

Use the existing `RoleId` and `SquadMemberId` in the successful configuration model:

- `SquadConfiguration.Roles` is an ordered `IReadOnlyList<RoleId>` and `Leader` is a `SquadMemberId`.
- `SquadMemberConfiguration.Name` is a `SquadMemberId` and `Role` is a `RoleId`.
- `SquadConfigurationLoader` keeps raw document strings until validation succeeds, then constructs the typed
  values once.
- `Ctx`, `WorkspacePreparer`, and `LaunchPreparer` carry those values without repeated wrapping or early
  unwrapping. Formatting is limited to prompt-file/path construction and other actual string boundaries.

**Acceptance:** role order, default and explicit leader selection, unknown role references, duplicate members, and
two members sharing one reusable role retain the behavior covered by `MemberConfiguration.feature`,
`LeaderConfiguration.feature`, and `RoleOrder.feature`.

**Status: complete (a4a1ebc6b8).** Validated `SquadConfiguration`/`SquadMemberConfiguration` now expose `RoleId` and
`SquadMemberId`. The loader still validates raw document strings, then constructs those identities once.
`Ctx`, `WorkspacePreparer`, and `LaunchPreparer` pass them through without re-wrapping.

### Slice 3 - Model the configured worktree target [done]

**Task:** `model-worktree-target`

**New type:** `WorktreeTarget` in `squad.Configuration`.

Represent either the project-root worktree or one named linked worktree without retaining `"master"` as an
internal sentinel. Parse the raw JSON value in `SquadConfigurationLoader`; make `SquadMemberConfiguration` carry
the typed target; and let `WorkspacePreparer` use that target for uniqueness checks, path resolution, worktree
creation/reset, shared-link preparation, and branch naming. The runtime `SquadMemberDefinition` should retain only
the resolved worktree path and must drop `WorktreeName`; command-side lookup should likewise retain only its
resolved path after parsing. Raw configuration builders continue to emit `"master"` or a linked-worktree name.

Treat only the documented `"master"` value as the project root. Any named value follows the linked-worktree path;
do not preserve the undocumented `"none"` branch in workspace preparation.

**Acceptance:** main-checkout context resolution, linked-worktree context resolution, shared-role worktree
isolation, shared worktree paths, launch reset/continue behavior, and handoff cleanup remain covered by
`Context.feature`, `MemberConfiguration.feature`, `HeadquartersWorkspaceFailures.feature`, and
`HandoffLaunchCleanup.feature`. No type other than `WorktreeTarget` is added.

**Status: complete (02e4055be3).** `WorktreeTarget` distinguishes the project-root worktree from a named linked
worktree. `SquadMemberConfiguration` carries it; `SquadMemberDefinition` keeps only the resolved path; workspace
preparation uses `IsProjectRoot`/`ResolvePath` and no longer special-cases `"none"`.

### Slice 4 - Type permission mode end to end [done]

**Task:** `type-permission-mode`

**New type:** `PermissionMode` in `squad.Domain`.

Define the provider-neutral choices `Prompt` and `ApproveAll`. Parse the raw `permissions` token in
`SquadConfigurationLoader`, default an omitted token to `Prompt`, and carry the enum through
`SquadMemberConfiguration`, `AgentSettings`, `AgentRoleContext`, launch composition, and the Copilot adapter.
Delete `SquadAgentConfiguration` if it becomes a duplicate of `AgentSettings`; do not replace it with another type.
Map the enum explicitly to Copilot behavior rather than comparing `"approveAll"` in the adapter.
`SquadConfigurationAgentDocument.Permissions` remains `string?`.

Add a configuration-author scenario proving an unsupported permission token is rejected with the existing
diagnostic before any session starts. Keep safe workspace reads and managed approvals unchanged.

**Acceptance:** `MemberConfiguration.feature`, `InteractionPublicationAndOwnership.feature`, and
`TranscriptFileReadSummaries.feature` pass with the exact JSON spelling and diagnostics unchanged. Update the
permission-mode and module manual text. No type other than `PermissionMode` is added.

**Status: complete (7157904f01).** `PermissionMode` is carried through settings, launch context, and the Copilot
adapter. `SquadAgentConfiguration` was deleted as a duplicate of `AgentSettings`. The JSON `permissions` token
stays a string; an unsupported token is rejected by `MemberConfiguration.feature` before any session starts.

### Slice 5 - Carry member identity through provider and application runtime

**Task:** `carry-member-identity-through-runtime`

**New types:** none.

Use the existing `SquadMemberId` for the configured participant in `AgentRoleContext`, `IAgentSession`, the Copilot
and fake session/runtime implementations, `SessionGeneration`, and every public/private member-routing operation
inside `SquadMembers`. Rename internal members from `Role`/`role` to `MemberId`/`memberId` where they identify a
configured participant. Provider and fake-control JSON continue to format the value as the existing `role` string.

Make `MemberSnapshot.Id` a `SquadMemberId`; remove its unused reusable-role property and remove the corresponding
unused state from `MemberAggregate`. The reusable `RoleId` remains in configuration and startup-instruction
composition, where it actually selects a role prompt.

Parse the Headquarters-control request's raw `role` into `SquadMemberId` before calling application readiness, and
type the fake provider observation journal's accepted member-keyed state after its control JSON is parsed. Keep
provider `SessionId` as string.

**Acceptance:** two members sharing one role still receive distinct sessions and worktrees; prompt, abort,
readiness, failure, and session disposal remain member-specific under `MemberConfiguration.feature`,
`PromptIsolationAndReadiness.feature`, `AbortOrdering.feature`, and `HeadquartersLifecycle.feature`.

### Slice 6 - Type UI member commands

**Task:** `type-ui-member-commands`

**New types:** none.

Change `ISquadUi`, `SquadViewModel`, and their application delegates to accept `SquadMemberId` for prompt, abort,
pending-elicitation lookup, and all interaction responses. `UiCommandHandler` must validate and wrap each incoming
wire `role` exactly once before dispatch. Snapshot composition must format member IDs only while building the
existing `role` and `leader` JSON fields; protocol errors continue to quote the original value.

**Acceptance:** command routing, unknown-member diagnostics, identical interaction IDs owned by different members,
and the versioned state snapshot remain byte-shape compatible under `UiProtocolValidation.feature`,
`InteractionPublicationAndOwnership.feature`, `PromptIsolationAndReadiness.feature`, and
`StdioUiProtocolMultiRole.feature`.

### Slice 7 - Type transcript member state

**Task:** `type-transcript-member-state`

**New types:** none.

Use `SquadMemberId` throughout `ITranscriptUi`, transcript snapshot/page/update/archived-entry records,
`TranscriptStore`, `GenerationTranscriptArchive`, `MemberTranscriptArchive`, `MemberTranscriptState`,
`TranscriptArchive`, `UiDeliveryCoordinator`, and `TranscriptAnnouncementJournal`. Rename internal `Role`
properties and dictionaries to `MemberId` terminology. `UiCommandHandler` parses incoming transcript member names
once; `TranscriptProtocol` is the sole formatter for outgoing `role` strings. Filesystem hashing receives
`memberId.Value` only at the path boundary.

**Acceptance:** synchronization, recovery announcements, paging, archive lookup, retention, and concurrent
multi-member updates preserve their ordering and isolation under `TranscriptSynchronizationOrder.feature`,
`TranscriptHistoryPaging.feature`, `TranscriptActiveStreamRetention.feature`, and
`StdioUiProtocolMultiRole.feature`.

### Slice 8 - Type accepted member state in specifications

**Task:** `type-spec-member-state`

**New types:** none.

After Gherkin/configuration/control strings have been accepted, use `SquadMemberId` for member-keyed fixture state:

- `BackendScenario`'s configured-member collection;
- `ScenarioWorkspace`'s member-to-worktree map;
- transcript page, delta, and sequence maps in `BackendScenarioSteps`;
- readiness waits in `HeadquartersLifecycleSteps`;
- synchronization state in `StdioUiProtocolSteps`;
- remaining member-keyed lifecycle, prompt, and observation state in `ObservationJournal`.

Keep `ScenarioWorkspace.myValues`, raw configuration builders, table cells, envelope observations, session IDs,
and protocol correlation keys as strings. Rename fixture APIs internally from role to member where they hold
configured identity, without changing Gherkin's established user-facing `role` vocabulary.

**Acceptance:** the focused configuration, lifecycle, multi-role UI, and transcript features used by slices 5-7
continue to compile and pass without changing feature wording.

### Slice 9 - Remove duplicated interaction ownership

**Task:** `remove-interaction-owner-duplication`

**New types:** none.

Remove the `Role` field from `AgentPermissionRequest`, `AgentInputRequest`, and `AgentElicitationRequest`. Provider
sessions publish requests without copying their member identity into every event; `SessionGeneration` already
routes the event through the owning session, and each `MemberAggregate` owns its pending collections. Compose the
existing snapshot `role` field from the owning `MemberSnapshot.MemberId`, not from an event payload that could
disagree.

**Acceptance:** permission, input, and elicitation publication and response routing remain isolated by member,
including identical request IDs on two members, wrong-member responses, cancellation, and reconnect retention in
`InteractionPublicationAndOwnership.feature` and `InteractionCancellationAndTranscriptRetention.feature`.

### Slice 10 - Type interaction request IDs

**Task:** `type-interaction-request-id`

**New type:** `InteractionRequestId` in `squad.AgentProvider.Abstractions`.

Use the opaque value object on all three interaction request events, all `IAgentSession` response methods,
application mailbox messages, `MemberAggregate` pending/protected-entry dictionaries, UI abstraction methods, and
Copilot pending-interaction dictionaries. Give it no invented format rule; it exists to prevent cross-kind ID
mixups. The Copilot and fake provider adapters wrap IDs after their SDK/control boundaries, `UiCommandHandler`
wraps `requestId` after envelope validation, and snapshot/fake-control JSON formats `.Value`.

Keep `UiMessage.RequestId`, fake-control envelopes, Gherkin values, and issue-catalog correlation IDs as strings.

**Acceptance:** all interaction ownership, duplicate/late response, cancellation, and UI validation scenarios
retain their exact wire IDs and diagnostics. No type other than `InteractionRequestId` is added.

### Slice 11 - Type elicitation mode

**Task:** `type-elicitation-mode`

**New type:** `ElicitationMode` in `squad.AgentProvider.Abstractions`.

Define `Form` and `Url`, use it on `AgentElicitationRequest`, and map provider SDK/fake-control spellings at their
input boundaries. Snapshot publication maps it back to lowercase `form`/`url`; `UiCommandHandler` decides whether
to open an accepted URL from the enum, never a string comparison. Preserve the existing provider default for an
omitted mode and reject unsupported provider/fake-control values explicitly rather than carrying them inward.

**Acceptance:** both form and URL requests retain their current state-snapshot shape and accepted URL behavior in
`InteractionPublicationAndOwnership.feature`. No type other than `ElicitationMode` is added.

### Slice 12 - Type elicitation action

**Task:** `type-elicitation-action`

**New type:** `ElicitationAction` in `squad.AgentProvider.Abstractions`.

Define `Accept`, `Decline`, and `Cancel`; use it on `AgentElicitationResponse`; and parse the UI wire action once in
`UiCommandHandler`. Map explicitly to the Copilot SDK and fake-control strings. Reject unsupported UI action tokens
as protocol errors before the provider is called, and only pass form content/open a URL according to the existing
accepted-action semantics.

Extend black-box interaction coverage to exercise all three supported spellings and one unsupported spelling
without exposing C# enum names.

**Acceptance:** provider observations remain lowercase `accept`, `decline`, and `cancel`; invalid actions produce a
protocol error and no provider response. No type other than `ElicitationAction` is added.

### Slice 13 - Normalize tool read capability

**Task:** `normalize-tool-read-capability`

**New types:** none.

Replace `AgentToolStartedEvent.Kind` with an explicit provider-neutral `bool IsRead`. Parse the fake-control
`toolKind` string at that adapter boundary and normalize any SDK capability there; retain tool-name compatibility
tables as strings because provider/plugin tool names are open vocabulary. `MemberEventProjector` branches only on
the boolean plus the existing known-name compatibility classification, never on `"read"`.

Add a black-box case in `TranscriptFileReadSummaries.feature` using an otherwise unknown tool name explicitly
classified as a read, proving the boundary normalization while preserving all existing read suppression and line
count behavior.

**Acceptance:** read output never leaks, ordinary tools still stream output, and no provider-neutral event carries
a raw kind token.

### Slice 14 - Type tool-call IDs

**Task:** `type-tool-call-id`

**New type:** `ToolCallId` in `squad.AgentProvider.Abstractions`.

Use the opaque value object on every normalized tool lifecycle event and all tool-correlation state in the Copilot
normalizers and `MemberTranscriptState`. Wrap SDK and fake-control strings once at adapter ingress; format only in
raw diagnostics/control JSON. Do not add format validation, and keep tool names as strings.

**Acceptance:** interleaved output remains attached to the correct entry, progress/replacement/completion ordering
is unchanged, and read summaries still correlate under `TranscriptToolCallCorrelation.feature`,
`TranscriptToolLifecycleState.feature`, `TranscriptToolOutputAggregation.feature`, and
`TranscriptFileReadSummaries.feature`. No type other than `ToolCallId` is added.

### Slice 15 - Type transcript source

**Task:** `type-transcript-source`

**New type:** `TranscriptSource` in `squad.Ui.Abstractions`.

Define the complete current set (`Harness`, `User`, `Assistant`, `Reasoning`, `System`, `Error`, `Tool`, `Read`,
and `Subagent`) and use it in `TranscriptEntry`, streaming buffers, projection, live state, and archive
reconstruction. `TranscriptProtocol` explicitly emits the existing lowercase spellings. The archive persists and
parses those same stable spellings rather than relying on default enum serialization; unsupported archived values
fail explicitly.

Keep wire observations in test support and frontend presentation state as strings.

**Acceptance:** every source retains its exact lowercase UI and archive representation across updates,
synchronization, paging, replacement, and process-lifetime history in `TranscriptProtocolShape.feature`,
`TranscriptFileReadSummaries.feature`, `TranscriptSubagentPresentation.feature`,
`TranscriptSkillActivityPresentation.feature`, and transcript paging/retention features. No type other than
`TranscriptSource` is added.

### Slice 16 - Type handoff participant identities

**Task:** `type-handoff-member-identities`

**New type:** `ScalarJsonConverter<TValue, TScalar>` in `squad.Handoffs`.

Introduce this one narrow converter for explicitly mapping typed handoff values to their existing scalar JSON
representations; it must not use reflection and will be reused by later handoff value-object slices. In this slice,
register it only for the existing `SquadMemberId`, then change `HandoffDocument.From`, `To`, and `Recipient`,
delivery maps/tuples, `IRoleNotifier`, command construction/recipient validation, and valid parsed
`QueuedHandoff` observations to use `SquadMemberId`.

The handoff serializer must still emit JSON strings named `from`, `to`, and `recipient`; filenames and CLI/Gherkin
arguments remain strings at their boundaries. The specification observer must continue parsing JSON independently
of `HandoffJson`.

**Acceptance:** fan-out, unknown-recipient rejection, recipient headers, wake-ups, and shared-role/member
distinction preserve existing behavior in `Handoffs.feature`, `Delivery.feature`, and
`MemberConfiguration.feature`. No type other than `ScalarJsonConverter<TValue, TScalar>` is added.

### Slice 17 - Type handoff IDs

**Task:** `type-handoff-id`

**New type:** `HandoffId` in `squad.Handoffs`.

Make generated and deserialized handoff identity explicit from creation through validation and delivery. Use the
existing scalar converter to preserve the JSON string and keep the generated filename/ID spelling unchanged.
Type the ID in valid semantic `QueuedHandoff` observations while keeping their JSON parsing independent. Reject
missing or blank IDs as invalid handoff documents with the existing wrapped `InvalidDataException` surface.

**Acceptance:** valid handoffs preserve their exact JSON shape and delivery behavior, while a raw handoff with a
missing/blank ID is archived as failed before fan-out. No type other than `HandoffId` is added.

### Slice 18 - Type handoff priority

**Task:** `type-handoff-priority`

**New type:** `HandoffPriority` in `squad.Handoffs`.

Replace the static `Priority` helper and primitive `HandoffDocument.Priority` with a value object that owns the
inclusive `0..99` invariant, exact two-digit CLI parsing/formatting, numeric JSON conversion through the existing
scalar converter, and filename ordering representation. Preserve aggregated CLI validation: an invalid priority
must still be reported alongside independent recipient/option errors rather than throwing early.

Use the typed priority in task/batch selection and valid semantic mailbox observations. Raw malformed JSON remains
independently authored by the fixture.

**Acceptance:** default/overridden priority, invalid CLI priority, lexical queue ordering, equal-priority batching,
and out-of-range JSON rejection are covered by `Handoffs.feature`, `TaskQueue.feature`, `BatchQueue.feature`, and
`Delivery.feature`. JSON `priority` remains a number and filenames remain two-digit-prefixed. No type other than
`HandoffPriority` is added.

### Slice 19 - Type canonical Git commit IDs

**Task:** `type-git-commit-id`

**New type:** `GitCommitId` in `squad.Handoffs`.

Keep the raw `--commit` revision as text until Git resolves it, then construct a `GitCommitId` for the canonical
ten-hex-character abbreviation and carry it in `GitHandoffData`, rendering, and semantic mailbox observations.
Use the existing scalar converter so durable JSON and `merge_and_process <sender> <commit>` remain strings with
the same spelling. Deserialized handoffs with a noncanonical commit ID must fail validation before delivery.

**Acceptance:** HEAD, explicit revision, dirty-worktree override, invalid revision, payload rendering, and malformed
durable commit coverage pass in `Handoffs.feature` and `Delivery.feature`. No type other than `GitCommitId` is
added.

### Slice 20 - Type handoff lifecycle timestamps

**Task:** `type-handoff-timestamps`

**New types:** none.

Change `CreatedAt`, `EnqueuedAt`, `DequeuedAt`, and `CompletedAt` to `DateTimeOffset`/`DateTimeOffset?` throughout
the handoff model, queue transitions, and valid semantic observations. Reuse the existing scalar converter with
explicit invariant parsing/formatting so the durable ISO-8601 strings keep the established UTC spelling; keep the
compact filename timestamp and delivery-log timestamp formatting as explicit string boundaries.

**Acceptance:** malformed timestamp JSON is rejected before delivery, and creation/delivery/claim/completion
preserve earlier instants exactly under `Delivery.feature`. No new type is added.

### Slice 21 - Validate persisted handoff text invariants

**Task:** `validate-persisted-handoff-text`

**New types:** none.

Make `HandoffDocument.Validate` enforce the same required and maximum-80-character rules for
`gitHandoff.task` and `note.message` that the CLI enforces. Keep human-authored task/message content as strings and
retain aggregated validation for kind/variant mismatches. Add raw durable-document scenarios proving empty and
overlong values are rejected before any recipient copy or wake-up.

**Acceptance:** CLI diagnostics remain unchanged and malformed persisted documents are archived as failed under
`Handoffs.feature` and `Delivery.feature`.

### Slice 22 - Centralize handoff queue layout

**Task:** `centralize-handoff-queue-layout`

**New types:** none.

Extend the existing `HandoffQueue` owner with semantic path methods/constants for the root, outbox, sent, failed,
new inbox, in-process inbox, and completed inbox. Replace literal path assembly in role commands, workspace
preparation/cleanup, delivery, and mailbox fixture support. Do not introduce a bucket enum or pass-through layout
class. Filesystem paths remain strings at `System.IO` calls, and black-box feature text that names the documented
layout remains literal so it can still detect a wrong implementation path.

**Acceptance:** task/batch transitions, delivery/failure archiving, collision handling, and launch cleanup use the
same documented directories under `TaskQueue.feature`, `BatchQueue.feature`, `Delivery.feature`, and
`HandoffLaunchCleanup.feature`.

### Slice 23 - Parse handoff command kind once

**Task:** `parse-handoff-kind-once`

**New types:** none.

Map the first CLI token `commit | note` once to the existing `HandoffKind`, then use that enum for help selection,
option validation, dispatch, document construction, and valid semantic `QueuedHandoff` observations. Keep raw
option dictionaries, CLI tokens, and independently parsed JSON tokens as strings until validation succeeds.
Unknown commands and all usage/error text remain unchanged.

**Acceptance:** both command forms, per-kind help, invalid command, option-shape errors, and resulting document kind
remain covered by `Handoffs.feature`.

### Slice 24 - Type Headquarters readiness status

**Task:** `type-agent-readiness-status`

**New type:** `AgentReadinessStatus` local to `squad.Runtime.Control`.

Parse the control protocol's `ready`, `not-ready`, `unknown-role`, and `initializing` messages into this enum inside
`QueryAgentStatusAsync`; represent local unavailable/endpoint-unavailable outcomes with explicit enum members too.
`WaitForAgentAsync` must branch only on the enum and derive its existing user-facing timeout detail from an
exhaustive switch. The version-1 control request/response JSON remains string-based, and an unknown successful
status token is invalid protocol data rather than a new implicit state.

**Acceptance:** ready, busy timeout, unknown member, initialization, stale metadata, and unavailable endpoint
behavior remain covered by `HeadquartersOwnership.feature`, `PromptIsolationAndReadiness.feature`, and
`HeadquartersEarlyShutdown.feature`. No type other than `AgentReadinessStatus` is added.

### Slice 25 - Use cancellation for closed command admission

**Task:** `use-cancellation-for-closed-admission`

**New types:** none.

Replace the prose-discriminated `InvalidOperationException("Squad is shutting down")` control flow in
`SquadViewModel`, `SquadMembers`, and `MemberProcessor` with `OperationCanceledException` carrying the same
user-facing message. `SessionGeneration` must handle cancellation by type and remove its exception-message filter.
Unknown members and genuinely invalid operations remain `InvalidOperationException`; do not add a custom
exception or broad catch.

**Acceptance:** a command admitted before shutdown drains to its canceled result, a later command is rejected with
the existing "shutting down" protocol error and no side effect, and event observation stops cleanly under
`ShutdownCommandAdmission.feature` and `HeadquartersEarlyShutdown.feature`.

### Slice 26 - Type rejected-command effect in the UI fixture

**Task:** `type-rejected-command-effect`

**New type:** `RejectedCommandEffect` in `squad.Specs`.

Replace `BackendScenarioSteps.myLastInvalidMessageCase` with the small enum describing the provider effect that
must remain absent (`Prompt` or `PermissionResponse`). Map the scenario-outline case string to that enum at the
binding boundary and switch on the enum in the assertion. Do not turn raw envelopes or scenario example text into
product types.

**Acceptance:** every example and the unknown-member case in `UiProtocolValidation.feature` retains its existing
error and no-provider-side-effect assertion. No type other than `RejectedCommandEffect` is added.

### Slice 27 - Document and verify retained primitive boundaries

**Task:** `document-retained-string-boundaries`

**New types:** none.

Move the durable rationale from this issue's "Strings intentionally retained" table into the smallest appropriate
`docs/Manual` architecture/module documentation, updated to the final type and property names. Audit product and
specification dictionaries, parameters, return values, comparisons, and switches that still use strings. Remove
any remaining premature `.Value` conversion; for every retained semantic-looking string, either move it to the
correct typed slice above or document why it is raw text, open vocabulary, an external encoding, a platform value,
or a fixture property-bag key.

The final audit must specifically confirm that:

- no member-keyed C# state remains keyed by `string` after a boundary has parsed the member;
- no interaction/tool correlation state remains keyed by `string`;
- no closed permission, elicitation, transcript-source, handoff-kind, priority, readiness, or shutdown choice is
  controlled by string comparison inside its owning module;
- raw DTOs, malformed-input fixtures, protocol envelopes, exact wire observations, frontend state, human text,
  provider/plugin names, platform values, CLI syntax, provider session IDs, and private fixture property-bag keys
  remain strings for the reasons documented.

**Acceptance:** the solution builds, the complete acceptance suite passes once, the manual accurately describes
the final boundaries, and no behavior or protocol shape changed unintentionally.

After Slice 27 is accepted, the architect deletes this issue.
