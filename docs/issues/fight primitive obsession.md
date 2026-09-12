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

