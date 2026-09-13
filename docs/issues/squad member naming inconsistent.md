---
title: squad member naming inconsistent
priority: 3
---

## Decision

Use these terms consistently:

- A **role** is a reusable stereotype declared in the configuration's `roles` catalog and described by
	`blaxquad/roles/<role>.prompt`.
- A **squad member** is a configured participant with its own identity, worktree, receive mode, agent settings,
	provider session, state, interactions, and transcript.
- Once Headquarters resolves a configured participant, runtime code and UI presentation must call it a member, not
	a role.

This is a terminology refactor only. It must not change configuration, cardinality, identity, routing, ordering,
timing, persistence, or any other behavior.

## Current-state analysis

The domain model already has the right distinction: `RoleId` represents the reusable prompt-backed role, while
`SquadMemberId`, `SquadMemberDefinition`, `SquadMember`, `SquadMemberProcessor`, and `SquadMembers` represent
configured and running participants. Most remaining inconsistencies are older names around that member-owned model.

The current branch also already contains acceptance coverage for two members referencing one role in
`MemberConfiguration.feature`. That is existing behavior, not work for this issue. This refactor must neither add to
nor change that behavior.

### Backend

The following types are member-scoped even though their names still say role:

- `AgentRoleContext` and `AgentBackendContext.Roles` carry one entry per configured member. Each entry already has a
	`SquadMemberId`, member worktree, member settings, and one provider session. Their consumers are
	`LaunchPreparer`, `CopilotSdkAgentRuntime`, and `FakeAgentRuntime`.
- `RoleTranscriptSnapshot`, `RoleTranscriptPage`, and `RoleArchivedTranscriptEntry` all carry a `SquadMemberId`.
	`RoleArchiveState`, `TranscriptArchive.myRoles`, `GetRole`, and related locals likewise hold per-member transcript
	state.
- `IRoleNotifier` and `SessionRoleNotifier` wake a recipient identified by `SquadMemberId` after handoff delivery.
- `GetRoleReadiness*`, `MarkRoleFailedAsync`, `RouteIgnoringUnknownRoleAsync`, and `EnsureRoleAvailable` operate on
	member aggregates and sessions.
- Provider, fake-provider, Headquarters-control, workspace, and CLI internals still use `role` for parameters,
	collections, comments, and diagnostics whose value is actually a member identity.
- `CurrentRoleResolver` resolves a configured member from its worktree. Test support has the same problem in helpers
	such as `RoleWorktreePath`, `WaitForRoleStatusAsync`, and `RoleSessionNeverStarted`.

The genuine role concepts must remain unchanged: `RoleId`, `SquadConfiguration.Roles`, `member.Role`, role prompt
validation, `RolesDir`, and prompt construction from `blaxquad/roles/<role>.prompt`.

### UI and UI protocol

The frontend currently uses the wire representation directly as presentation state. Consequently the legacy wire
field `role` leaks into all layers:

- `RoleSnapshot`, `RoleState`, and `RoleTranscriptSynchronization` describe member state.
- `RolePanel.vue` and `RoleHeader.vue` render one member, not one role.
- `useRoleInteractions`, `useFocusedRoleAbort`, `roles`, `rolePanels`, `rolesWithOlderTranscript`, prompt-history
	maps, transcript-history maps, and announcement maps are all keyed by member identity.
- `.role-grid`, `.role-panel`, and `.role-error`, plus labels such as `Agent roles` and `Dismiss role error`, describe
	members.
- `PromptComposer` and `InputRequest` receive a member identity through props currently named `role` or `roleName`.

The UI JSON protocol is a stable boundary and currently uses `role`, `roles`, and `role.abort`. The C# side already
converts those strings to `SquadMemberId` before dispatch. Those wire spellings must not be renamed in this issue;
instead, the protocol adapter must contain them and map them immediately to member-named frontend and backend
models. The same rule applies to the Headquarters-control wire values `role` and `unknown-role`.

### Specifications, tests, and documentation

Runtime-oriented Gherkin still says `role` for sessions, status, prompts, interactions, transcripts, handoff
recipients, worktrees, and dashboard panels. Step definitions and support code repeat the same terminology. In
contrast, configuration scenarios that discuss the reusable role catalog, a member's role reference, or a role
prompt are already correct and must retain `role`.

Playwright helpers and selectors expose the implementation names (`roleSnapshot`, `roleTranscript`, `.role-panel`)
even though the tests exercise member panels. ARIA's HTML `role` attribute and Playwright's `getByRole` API are web
platform terms and are not part of this rename.

The glossary defines role and squad member correctly at the top, but later entries regress to role session, role
state, role transcript, role interaction, role worktree, and role handoff terminology. The same drift appears in
`architecture.md`, `modules.md`, `test-strategy.md`, and parts of `README.md`.

## Compatibility and scope guardrails

- Do not edit `blaxquad/squad.json` or change its schema. Keep the public `roles` catalog and `members[].role`
	reference exactly as they are.
- Do not change `RoleId`, role prompt names or paths, role lookup/validation, or any code that genuinely operates on
	reusable role definitions.
- Do not add, remove, constrain, or otherwise modify the relationship between roles and members. Preserve the
	current behavior exactly, including the current branch's existing shared-role behavior.
- Keep stable serialized and command spellings unchanged: UI protocol `role`/`roles` fields and `role.abort`,
	Headquarters-control `role` and `unknown-role`, `squad context --field role` and its JSON fields, and existing
	handoff document fields. Treat them as compatibility vocabulary at an untyped boundary, not as domain names.
- Do not add dual-read aliases, schema migration, protocol versioning, or fallback behavior. That would expand the
	accepted behavior rather than perform a refactor.
- Do not change member IDs, display names, ordering, session count/lifecycle, command routing, transcript sequence
	or retention rules, handoff paths/content, prompt text, CSS layout values, or accessibility behavior. The only
	intended user-visible differences are corrected member terminology in human-readable UI/help/diagnostic text.
- Do not edit generated output under `bin`, `obj`, `dist`, or Playwright result directories. Regenerate build output
	through the normal commands.

## Implementation plan

### 1. Lock the behavior-preserving boundaries

- Keep the existing configuration examples and `MemberConfiguration.feature` assertions intact as a guard that
	configuration parsing and member/role association did not change.
- Make the existing UI-protocol shape scenarios explicitly retain the raw `role`, `roles`, and `role.abort`
	spellings while changing scenario prose to call the addressed participant a member.
- Keep `Context.feature` and Headquarters-control scenarios asserting their existing structured field/token names.
	Update only prose and helper names that describe the value as a runtime role.

### 2. Rename backend member-owned types and operations

- Rename `AgentRoleContext` to `AgentMemberContext` and `AgentBackendContext.Roles` to `Members`; update workspace
	preparation and both provider runtimes without changing construction order or session startup.
- Rename transcript DTOs to `SquadMemberTranscriptSnapshot`, `SquadMemberTranscriptPage`, and
	`SquadMemberArchivedTranscriptEntry`. Rename `RoleArchiveState` to `SquadMemberArchiveState`, and rename per-role
	archive/journal collections, limits, methods, parameters, and comments to per-member equivalents.
- Rename `IRoleNotifier` to `ISquadMemberNotifier` and `SessionRoleNotifier` to `SessionMemberNotifier`; keep delivery
	wake-up behavior and message text unchanged.
- Rename member operations such as `GetRoleReadiness*`, `MarkRoleFailedAsync`, `RouteIgnoringUnknownRoleAsync`, and
	`EnsureRoleAvailable` to their member equivalents. Update member-related comments and human-readable diagnostics,
	while preserving compatibility-bound protocol error text where it names a raw `role` field or token.
- Rename `CurrentRoleResolver` to `CurrentMemberResolver` and member-worktree locals/helpers accordingly. Keep the
	public configuration and `squad context` field names unchanged at their output/parsing boundary.
- Apply the same terminology to Copilot adapter diagnostics and fake-provider APIs/state. If the private fake
	provider control JSON keeps its existing `role` field for minimal churn, contain that spelling in its encoder and
	parser exactly as for the product protocols.

Use symbol-aware renames for C# types and methods so all cross-assembly references move together. Do not combine this
with module moves, new value objects, or unrelated cleanup.

### 3. Isolate legacy UI wire names from frontend state

- Split raw protocol DTOs from presentation models in `src/squad-ui/src/protocol`. Raw envelopes retain the exact
	current JSON shape; decoded models use `memberId`, `MemberSnapshot`, `MemberState`, and
	`MemberTranscriptSynchronization`.
- Decode inbound `role`/`roles` fields once in the bridge before data reaches composables. Encode outbound member IDs
	back to the existing `role` field in the bridge. Keep all message types and payload contents unchanged.
- On the C# side, rename `UiMessage.Role` and related locals to member-oriented names after `UiMessageReader` reads
	the legacy `role` field. Keep `TranscriptProtocol` and state snapshot output keys unchanged while renaming their
	types, parameters, and loop variables.
- Apply the same adapter pattern to `HeadquartersControlRequest`: use member naming after parsing, but continue to
	read and write the existing control JSON.

This boundary mapping is what allows the UI and backend to stop treating a member as a role without changing the
protocol observed by the packaged dashboard, alternate clients, or black-box specifications.

### 4. Rename the frontend presentation model

- Rename `RolePanel.vue` to `MemberPanel.vue` and `RoleHeader.vue` to `MemberHeader.vue`; rename props and local state
	from `role` to `member`, using `memberId` for string identities.
- Rename `useRoleInteractions` and `useFocusedRoleAbort` to member equivalents. Rename member-keyed state throughout
	`useDashboardSession`, `useTranscriptFeed`, `useTranscriptHistory`, `useTranscriptAnnouncements`,
	`usePromptHistory`, and `useInteractionDrafts`.
- Rename related `App.vue`, `PromptComposer.vue`, `InputRequest.vue`, and workspace-toolbar props, refs, callbacks,
	and computed values, including focused/target member naming.
- Rename `.role-grid`, `.role-panel`, and `.role-error` to `.member-grid`, `.member-panel`, and `.member-error` without
	changing any declarations. Update visible and accessible labels to squad-member terminology.
- Leave HTML/ARIA `role` attributes and Playwright `getByRole(...)` calls alone.

### 5. Refactor tests and specification vocabulary

- Rename Playwright helpers to `memberSnapshot` and `memberTranscript`, update member-panel selectors, and use member
	names throughout test variables. Keep raw protocol fixtures in an explicitly named wire layer with their existing
	`role`/`roles` keys.
- Rewrite Gherkin steps that address a running participant: member session, member status, member transcript, member
	prompt/abort/interaction, member worktree, member handoff sender/recipient, and member readiness.
- Retain role wording in configuration catalog/reference/prompt scenarios. For generic setup tables whose `role`
	column merely creates a same-named member and role, rename the test DSL to make the member explicit while keeping
	the generated product configuration byte-for-byte equivalent.
- Rename binding and support APIs such as `WaitForRoleStatusAsync`, `RoleSessionNeverStarted`, `RoleWorktreePath`,
	and role-keyed observation helpers. Keep raw protocol parsing assertions at the boundary.
- Rename feature files whose subject is runtime members, for example `RoleOrder.feature` and
	`StdioUiProtocolMultiRole.feature`; do not rename configuration features whose subject is genuinely roles.
- Do not add a new role-sharing scenario for this issue. The existing scenario is only a regression guard against
	accidental behavior changes.

### 6. Update stable documentation

- Update `docs/manual/glossary.md` so dashboard panels, agent sessions/events, state, transcripts, interactions,
	handoff endpoints/queues/delivery, receive modes, and session generations belong to squad members.
- Update the same concepts in `architecture.md`, `modules.md`, `test-strategy.md`, and `README.md`.
- Retain and sharpen the explicit compatibility note explaining why legacy public fields still say `role` even
	though their value is a member ID. Do not present those spellings as the domain model.

### 7. Audit the residue

Search source, tests, and stable documentation for `role`/`Role` after the refactor. Every remaining occurrence must
fall into one of these reviewed categories:

1. reusable role catalog, `RoleId`, member role reference, or role prompt;
2. unchanged public configuration;
3. explicitly isolated compatibility wire/CLI spelling;
4. HTML/ARIA or third-party/provider terminology;
5. generated output, which is not edited directly.

Any runtime member state, session, transcript, interaction, panel, worktree owner, readiness result, notifier, or
test helper outside those categories is unfinished work.

## Validation

Run validation after each dependency slice, then run the complete gates:

1. Build the full solution to catch cross-assembly type and filename renames.
2. Run the black-box `squad.Specs` acceptance suite with `src/test.runsettings`. The existing member configuration,
	 UI protocol, control, context, lifecycle, interaction, transcript, and handoff scenarios must retain the same
	 outcomes and raw contract spellings.
3. Run `npm run build` in `src/squad-ui` to catch Vue prop/import and TypeScript model mismatches.
4. Run the complete Playwright suite with `npm run test:browser`. In particular, retain multi-panel layout,
	 per-member draft/history isolation, focused-member abort, interaction routing, transcript synchronization,
	 paging, scrolling, virtualization, and accessibility behavior.
5. Run the final terminology residue audit described above and review the diff for accidental configuration,
	 protocol, timing, or persistence changes.

## Acceptance criteria

- Runtime/backend code uses member terminology for member identity, state, sessions, readiness, notifications, and
	transcripts; role terminology remains only where it means a reusable role or an explicitly documented legacy
	boundary spelling.
- The Vue presentation model, components, composables, CSS classes, and user-facing labels use member terminology.
- Gherkin and Playwright tests distinguish configuration roles from running members and continue to test behavior
	only through the established black-box boundaries.
- The glossary and other stable manual pages use the same definitions consistently.
- `blaxquad/squad.json`, its accepted schema, and its role/member relationship are untouched.
- Existing UI, CLI, control, provider, handoff, transcript, and lifecycle behavior remains unchanged, and all build,
	acceptance, and browser-test gates pass.
