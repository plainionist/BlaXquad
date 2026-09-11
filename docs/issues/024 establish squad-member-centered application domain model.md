---
title: Establish a squad-member-centered application domain model
priority: 30
---

# Establish a squad-member-centered application domain model

## Decision

Treat the application-domain restructuring and the configuration split between roles and squad members as one
behavior-preserving internal refactoring. Do not combine it with an externally visible CLI, control-protocol,
UI-protocol, or dashboard terminology migration.

The implementation must establish the correct internal identities once rather than first consolidating mutable state
under a role identity and later splitting that identity into a reusable role and an operational squad member. At the
existing public boundaries, compatibility adapters keep the current role-shaped commands, messages, labels, and
observable behavior unchanged. Only `blaxquad/squad.json`, configuration-focused acceptance coverage, and test
workspace builders that emit that file may require adaptation.

The target model must establish the correct identities and lifetimes first: Headquarters is the long-lived process,
it owns at most one active squad generation, a squad contains squad members, and each squad member performs one role.
Multiple squad members may perform the same role.

The lifetime boundary is part of the same restructuring. Building member aggregates inside process-lifetime objects
would make a later squad restart move them again. A replaceable `Squad` must own its members and generation resources
from the start, while Headquarters owns only the shell and replacement authority.

## Problem

Today, `role` means two different things:

- a reusable responsibility and its prompt, such as `coder`; and
- one configured participant with a worktree, agent settings, provider session, transcript, interactions, and
  operational state.

The first meaning is genuinely a role. The second is a squad member.

This conflation is present from configuration through presentation. A `roles` configuration entry combines the role
name and prompt convention with member-specific settings. The same role string then identifies a worktree, provider
session, application state, transcript, interaction recipient, handoff recipient, protocol panel, and UI component.
The model therefore assumes that every role has exactly one member.

At runtime, member-owned state is also distributed across squad-wide managers. `SquadViewModel` owns or coordinates
parallel role-keyed collections for `AgentRoleState`, provider sessions, pending interactions, operations, failures,
event projection, and snapshots. `PendingInteractionRegistry` and `RoleOperationCoordinator` recreate individual
members through their own role-keyed dictionaries and sets.

These are three aspects of the same missing boundary:

1. The domain has no explicit squad-member identity distinct from a role.
2. One member's mutable state has no single cohesive owner.
3. One active squad's lifetime and resources are not separated from the Headquarters process that hosts it.

Renaming types without changing ownership would preserve the distributed aggregate. Consolidating state under the
current role key would encode the wrong cardinality and require another restructuring before two coders could join
the same squad.

Likewise, adding restart around the current ownership would require Headquarters to coordinate and reset a loose set
of backend, session, handoff, ViewModel, and interaction resources. A restartable model instead replaces one complete
active `Squad` object.

## Current model

### Configuration and identity

Each entry in `blaxquad/squad.json` is called a role but contains both kinds of data:

- role data: the responsibility name and the prompt selected through `blaxquad/roles/<role>.prompt`;
- member data: unique operational identity, display name, worktree, receive mode, model, effort, and permissions.

The role name is consequently required to be unique and is reused as the address at every later boundary. The
`leader` setting also names this conflated identity.

### Application ownership

`AgentRoleState` and `RoleTranscriptState` are already scoped to one operational participant, despite their names.
They own visible provider state and transcript state respectively. Other state for the same participant is held by
squad-wide objects:

- `PendingInteractionRegistry` has separate role-keyed maps for permissions, input requests, elicitations, and
  protected transcript entries;
- `RoleOperationCoordinator` has role-keyed semaphores, cancellation sources, invalidation, abort, and failure state;
- `AgentEventProjector` mutates the selected state object and the squad-wide interaction registry; and
- `SquadViewModel` routes commands, serializes commits through one shared channel, exposes mutable role objects, and
  assembles snapshots from those separate owners.

The repeated keys compensate for the missing member aggregate. Transitions such as registering an interaction and
protecting its transcript entry, aborting an operation, or terminally failing a member cross several owners and
synchronization boundaries.

### Global serialization

Provider event order is meaningful within one member. There is no business requirement to order a coder member's
events against an unrelated reviewer member. The shared application command channel nevertheless creates a total
order, and queued delegates may await provider I/O. A slow member can therefore delay projection and interaction
handling for another member.

### Process and squad lifetime

`SquadApplication` has process lifetime but directly constructs and retains the backend, handoff pump,
`SquadViewModel`, and `SquadRuntimeController`. The controller owns a `SessionGeneration`, while other state belonging
to that generation remains in process-owned collaborators. There is no single object that represents the complete
active squad and can be stopped, disposed, and replaced while the window and Headquarters control endpoint remain
alive.

This makes an explicit restart a lifecycle redesign rather than a normal domain operation. It also leaves no
authoritative path for the issue explorer to begin each new issue with fresh agent sessions. Reconstructing selected
resources in place would risk retaining old member state or allowing an old asynchronous result to mutate the
replacement.

## Target domain model

Names below define application-domain meaning. Internal C# types and the version-2 configuration use these terms
consistently. Existing public CLI, control-protocol, UI-protocol, Gherkin, and Vue surfaces retain their current
role-shaped compatibility vocabulary in this issue and translate at the application boundary.

```text
Headquarters (process lifetime)
|-- process shell: window, UI connection, control endpoint, host lease
|-- workspace services: issue catalog and durable stores
`-- active-squad slot and replacement authority
  `-- Squad generation (zero or one active)
    |-- generation identity and optional issue context
    |-- backend runtime, command admission, observers, and handoff participation
    |-- role catalog
    |   `-- Role definition (immutable, reusable)
    |       |-- role identity
    |       `-- role prompt
    `-- member directory: member identity -> member processor
      `-- Squad member (configured participant)
        |-- member identity and display name
        |-- role reference
        |-- worktree and receive mode
        |-- agent settings and provider-session association
        `-- member aggregate (transient authoritative state)
          |-- projected agent state and readiness
          |-- transcript projection and retention protection
          |-- pending interactions
          |-- operation, cancellation, and abort state
          `-- failure state
```

The cardinalities are:

- one Headquarters process owns zero or one active squad at a time;
- one Headquarters process may own successive squad generations during its lifetime, but never overlapping active
  generations;
- one squad has one or more squad members;
- every squad member references exactly one role;
- one role may be referenced by one or more squad members; and
- every active squad member has at most one provider session in its squad generation.

For example, `coder-a` and `coder-b` are distinct member identities. Both may reference the `coder` role and read the
same `coder.prompt`, while retaining independent worktrees, agent settings, sessions, transcripts, interactions,
status, usage, and failure state.

### Headquarters

Headquarters is the long-lived executable shell. It owns process startup and shutdown, project ownership, the window
and UI transport, the control endpoint, process-wide platform resources, durable workspace services, and the
serialized authority that installs or replaces the active squad.

Headquarters does not own live member state, provider sessions, the backend runtime handle, session observers,
pending interactions, or the handoff pump for an active generation. It owns one reference to the current `Squad` and
can remain running while that slot is empty, starting, being replaced, or failed. A squad failure is therefore not
automatically a Headquarters-process failure.

### Squad

The active squad is a replaceable runtime generation created from the current squad definition. It is both the
configured team for that generation and the application-level routing and resource-ownership boundary. It owns:

- its generation identity and configuration snapshot;
- an immutable catalog of role definitions;
- a directory of configured members keyed only by unique member identity;
- squad-level policy such as which member is the leader;
- the backend runtime handle and member sessions;
- generation-scoped command admission, session observation, and handoff-pump participation;
- member processors and all transient member state; and
- composition of immutable member snapshots into the squad read model.

The member directory is the only application-domain collection keyed by member identity. After routing selects a
member, member-local APIs do not receive another member or role parameter.

The squad exposes one cohesive start operation and one idempotent, failure-collecting retirement operation. It does
not ask Headquarters to stop its sessions, clear its interactions, drain its observers, or dispose its backend in
independent steps. Retirement keeps ownership until every generation resource is conclusively stopped or reports an
explicit uncertain result.

### Squad lifecycle and replacement

Headquarters process lifecycle and active-squad lifecycle are separate state machines. Stopping a squad does not stop
the window, control endpoint, issue catalog, project lease, or Headquarters process. Stopping Headquarters first
retires the active squad and then releases process resources.

Restart means replacement, not resetting a collection of objects in place:

1. serialize the replacement request and close command admission for the current generation;
2. retire the complete current squad, including handoff activity, commands, sessions, observers, processors, and
  transient member state;
3. create a new squad generation from the current configuration and role prompts;
4. start its members and recover durable work;
5. atomically install it as active and publish its capabilities; and
6. acknowledge the initiating command.

No replacement generation may start until retirement of the prior generation is conclusive. If retirement or
startup fails, Headquarters remains alive, exposes the failure and retry capability, and retains any resource handle
whose termination is uncertain.

Manual restart and starting a selected issue use this same replacement operation. Starting an issue supplies the
issue context and prepares the leader member's issue prompt only after the new squad is running; it cannot send work
to the old squad or bypass replacement. This issue establishes that domain operation. The separate restart-button
issue owns its protocol and user interface.

### Role

A role is a reusable responsibility and instruction set, such as architect, coder, or reviewer. It is immutable
configuration, not a live agent and not an address for runtime state.

The role owns or identifies the role prompt. A squad with two coders does not duplicate `coder.prompt`. A role does
not own a worktree, provider session, transcript, pending interaction, operation, status, usage, or handoff mailbox.
Those belong to a squad member.

### Squad member

A squad member is one configured participant in a squad. Its unique member identity addresses commands, events,
handoffs, protocol messages, durable member state, and the corresponding UI panel. The member references a role and
owns member-specific execution configuration.

Each configured member has one application-domain aggregate for its squad generation. It owns all
transient state whose lifecycle follows that member:

- projected provider status, activity, error, model, effort, readiness, and usage;
- transcript entries, streams, sequence, tool correlation, retention, and member-bound archive access;
- pending permissions, input requests, and elicitations;
- the relationship between an interaction and its protected transcript entry;
- prompt and provider-operation admission;
- active-operation cancellation, abort coordination, and stale-event invalidation; and
- member-local failure and reset behavior.

An interaction is one member-local value keyed by request ID. Removing, restoring, or completing it cannot affect
another member, including another member with the same role.

### Member processor

Each member has one logical processor that is the sole mutable accessor to its aggregate. It receives typed member
commands and provider events and commits them in member order. Different processors can progress independently; the
domain defines no total order across members.

The processor distinguishes short state transitions from external operations. It may initiate provider I/O, but it
does not await long-running provider work while preventing later state messages from being considered. Completion,
failure, and cancellation return as typed messages carrying the member, squad generation, and operation identity needed
to reject stale results.

This is actor-style ownership but does not require an actor framework or the removal of every lock. The requirement
is one serialized mutable owner per member, explicit messages, bounded failure/backpressure behavior, and no implicit
cross-member serialization.

### Snapshots and publication

Mutable member state is not exposed outside its processor. Each committed transition produces or makes available an
immutable member snapshot containing internally consistent status, capabilities, usage, pending interactions, and
transcript position. Transcript updates retain an explicit per-member sequence.

An internal squad snapshot composes immutable member snapshots and carries the squad generation. It need not
represent one globally atomic instant across independent members. The existing process-lifetime UI adapter projects
that state into the unchanged version-5 snapshot only while the generation is active; this issue does not add an
active-squad phase or generation field to the wire contract. UI delivery may order and coalesce publications without
becoming another authoritative owner of member state.

Archived transcript history may outlive a squad generation in a durable Headquarters/workspace service. The active
squad owns only its live transcript projection and a generation-bound archive handle. Replacement preserves visible
history while preventing the retired aggregate from publishing further updates.

### Legitimate shared owners

Some concerns intentionally span members and remain outside member aggregates:

- squad generation identity, command admission, and session leasing;
- provider runtime ownership and generation-wide teardown inside the active squad;
- UI connection ordering, coalescing, and recovery;
- durable handoff delivery between sender and recipient members;
- shared transcript storage and storage limits; and
- worktree operation scheduling when several members intentionally share one checkout.

These owners use member identities because their invariants cross members. They must not duplicate authoritative
member state. Process-lifetime UI delivery and durable stores remain Headquarters services; generation-bound handles
are passed into the active squad. Issue 023 remains responsible for the additional scheduling and persistence rules
of shared worktrees, with its current use of `role` understood as the member identity that this issue introduces
explicitly.

## Naming and addressing rules

- Use `squad` for the configured team and its read model.
- Use `squad member` or `member` for a configured participant and live agent-facing state inside the application
  domain and version-2 configuration.
- Use `role` only for the reusable responsibility and role prompt.
- Use `agent session` for the provider-owned live conversation/execution resource used by one member.
- Use `Headquarters` for the process-lifetime shell and `Squad` for the replaceable active generation; a process owner
  must not be named `SquadApplication` in the target model.
- Member names are unique within a squad; role names do not identify a unique member internally.
- An internal domain field, parameter, map key, or route named `role` must not carry a member identity.
- Existing public role-shaped CLI and wire fields remain compatibility adapters in this refactoring and continue to
  carry the configured participant name without changing their serialized shape, command syntax, diagnostics, or UI
  behavior.
- Leader selection, worktree ownership, session routing, handoffs, transcripts, interactions, and UI panels are
  member-addressed inside the application even where an unchanged public adapter still calls that address `role`.
- Role prompts remain role-addressed and may be shared by multiple members.

This is not a global textual replacement. For example, `rolePrompt` remains correct, and the existing public
`RolePanel` remains unchanged in this issue; the application state behind that adapter becomes member-centered.

## Required invariants

- Headquarters can remain running with no active squad and can install successive squad generations without
  restarting process-wide resources.
- At most one squad generation is active. Replacement is serialized, and a new generation starts only after the old
  generation is conclusively retired.
- One active squad owns all generation resources and exposes one complete retirement contract; Headquarters does not
  dismantle those resources individually.
- Every command, event, readiness result, and asynchronous completion carries the squad generation and cannot mutate
  or publish through a replacement squad.
- Explicit restart and issue start invoke the same authoritative replacement operation. Issue work is prepared only
  after the replacement generation is running.
- Every provider event, user command, interaction response, abort, handoff, and operation completion is addressed to
  exactly one member at the squad boundary.
- After routing, member-local APIs accept neither a member nor role parameter, and member-local state holders contain
  no member- or role-keyed dictionaries.
- One member processor is the only writer of its aggregate; callers receive immutable values or snapshots.
- Member status, transcript mutation, pending-interaction mutation, transcript protection, operation state, and
  failure state change as one ordered transition where the domain operation relates them.
- Provider events and transcript updates preserve order within a member. No ordering dependency is introduced
  between unrelated members or between members that share a role.
- A blocked provider operation for one member cannot block state projection, interaction handling, or snapshots for
  another member.
- Same-member prompts and provider operations retain required serialization, cancellation, abort coalescing, and
  stale-event rejection behavior.
- Asynchronous results carry enough member, squad-generation, and operation identity to prevent an old operation from
  mutating replacement state.
- Session lifecycle and command admission have one squad-generation authority; Headquarters separately owns
  replacement admission. A member processor cannot outlive or bypass its squad generation.
- Two members sharing one role share no mutable runtime state merely because their role reference is equal.
- Durable handoff state, transcript history, issue files, worktrees, and process UI delivery may outlive a squad, but
  none becomes an alternate mutable owner of live member-domain state.

## Resolved implementation contracts

### Configuration schema and migration

Use an explicit version-2 configuration. Roles are reusable names whose prompts remain at
`blaxquad/roles/<role>.prompt`; ordered members carry all participant-specific settings:

```json
{
   "schemaVersion": 2,
   "leader": "architect",
   "roles": ["architect", "coder", "reviewer"],
   "members": [
     {
       "name": "architect",
       "displayName": "Architect",
       "role": "architect",
       "worktree": "master",
       "agent": {
         "model": "gpt-5.6-sol",
         "effort": "max",
         "permissions": "approveAll"
       }
     },
     {
       "name": "coder-a",
       "role": "coder",
       "worktree": "coder-a",
       "agent": {}
     },
     {
       "name": "coder-b",
       "role": "coder",
       "worktree": "coder-b",
       "agent": {}
     }
   ]
}
```

- `roles` is a non-empty array of unique role names. A role name identifies exactly one conventional prompt file.
- `members` is a non-empty ordered array. Member names are unique; each member references one declared role and owns
   its display name, worktree, receive mode, and agent settings. Repeating a role reference is valid.
- `leader` contains a member name and defaults to the first member when omitted or blank.
- Existing worktree uniqueness and safety rules continue to apply to members. Issue 023, not this issue, owns any
   later relaxation for deliberately shared worktrees.
- An optional `displayName` defaults from the member name. It is presentation metadata, not an address.
- A missing version or the legacy object-valued `roles` array is rejected with a migration diagnostic. Do not
   dual-read it as the new model. The documented migration preserves each old entry's `name` as the member name,
   adds that name to `roles`, sets the member's `role` to the same value, and leaves `leader` unchanged. This gives
   existing worktrees, handoffs, and scripts an explicit, identity-preserving path instead of silently changing what
   `role` means.

### Compatibility and testing boundary

- Preserve the provider SPI, Headquarters control protocol version 1, UI protocol version 5, handoff schema version
   1, every existing message type and field name, CLI syntax/output/diagnostic, dashboard label and interaction, and
   persisted runtime path. Boundary adapters may use legacy `role` vocabulary; the internal application values they
   route are member identities.
- Preserve the existing fake-provider control protocol and headless UI fixture vocabulary so the application
   refactoring does not force test rewrites.
- Do not edit existing behavioral Gherkin scenarios, step phrases, protocol expectations, or Playwright tests for
   this refactoring. Do not add aggregate, processor, concurrency, lifecycle, protocol, or frontend tests.
- Test changes are limited to configuration: workspace/spec builders must emit schema version 2, existing
   configuration-focused scenarios may be adapted to the new JSON shape, and focused configuration coverage may be
   added for reusable roles, same-role members, invalid references, uniqueness, leader selection, and the legacy
   migration diagnostic.
- All non-configuration behavior is protected by the unchanged existing black-box Gherkin and Playwright suites.
   The reviewer must treat behavioral test churn outside the configuration boundary, new internal-structure tests,
   or changed observable expectations as evidence that the slice exceeded this issue.

### Related-issue boundary

Issue 024 supplies the serialized active-squad replacement operation but adds no restart or start-issue command,
acknowledgement, button, or issue-explorer behavior. The `restart active squad` issue must expose this exact operation
through supported protocol and presentation boundaries and owns the first black-box scenarios that invoke a second
generation in one process. Do not add a direct-product-object test or test-only replacement trigger here.

Issue 027 must not run concurrently with this work. It is a later mechanical assembly consolidation and must consume
the resulting `Headquarters`/`Squad` ownership without restoring `SquadApplication` or duplicating member state.
Issue 024 does not perform that project move and does not change the version-1 control contract that issue 027
preserves.

## Implementation plan

Exactly one slice is active at a time. Each slice includes its production changes, obsolete production cleanup,
directly affected architecture documentation, and validation through supported black-box boundaries. Slice 1 is the
only slice allowed to adapt or add tests, and only for the configuration contract. Slices 2 through 4 run the
existing suites unchanged: they must not edit a feature, step definition, fake-provider/headless-client contract,
Playwright spec, or expected observable result.

### Slice 1 - Configure and launch reusable roles with distinct members [done]

**Outcome:** A configuration author can launch `coder-a` and `coder-b` as separate members that use the same `coder`
role prompt while retaining independent worktrees, settings, provider sessions, leader addressing, and startup
state.

- Implement the version-2 schema above with separate immutable role definitions and member configurations. Validate
   the schema version, unique role and member names, role-prompt existence, valid member role references, member
   worktree safety/uniqueness, agent settings, receive modes, and a leader member. Duplicate role references are
   deliberately valid.
- Carry separate member and role values through configuration and workspace preparation. Workspace creation,
   provider-session configuration, session registration, and event routing select the member; only prompt lookup and
   initial role instruction select the referenced role.
- Preserve the existing provider SPI, CLI, Headquarters control protocol, UI protocol, handoff schema, fake-provider
   control API, and dashboard behavior. Where one of those unchanged boundaries calls a participant address `role`,
   map the configured member name at that boundary rather than renaming the contract.
- Migrate `blaxquad/squad.json`, README/configuration examples, and test workspace builders to schema version 2. The
   command-side reader must consume `members` while preserving all current command output and diagnostics. Do not
   dual-read the legacy configuration to keep tests passing.
- Adapt or add only configuration-focused black-box coverage for the new schema: two members sharing one role,
   uniqueness and reference validation, leader-member selection/defaulting, missing prompts, and the explicit legacy
   migration diagnostic. Reuse the existing process/provider boundary; do not add member-isolation, protocol,
   concurrency, aggregate, or lifecycle scenarios in this slice.

**Acceptance:** The repository and generated test workspaces use only schema version 2. `coder-a` and `coder-b` can
start concurrently with different session IDs and worktrees, both receive the instruction for
`blaxquad/roles/coder.prompt`, and neither requires a `coder-a.prompt` or `coder-b.prompt`. Invalid configuration
starts no member session and reports the specific configuration diagnostic. Existing one-member-per-role lifecycle,
prompt, handoff, control, protocol, and UI scenarios remain byte-for-byte unchanged and green against the new test
workspace configuration.

**Status: complete (a0440689ef).** Schema version 2 separates role names from member configurations; `coder-a` and
`coder-b` share `coder.prompt` with distinct sessions and worktrees. Invalid documents (duplicate member, unknown
role, missing prompt, role-named leader, legacy v1) fail before any member session. Provider SPI and non-configuration
Gherkin stay role-shaped with member names mapped at the boundary. Slice 2 remains pending until the architect
activates it.

### Slice 2 - Make one aggregate own each member's mutable state [done]

**Outcome:** Every state transition for one member updates its status, transcript, pending interactions, operation
state, and failure state through one cohesive aggregate without consulting another member-keyed state holder.

- Introduce a member aggregate around projected agent state and member transcript state. It owns member metadata,
   readiness, transcript sequence/retention, pending permissions/inputs/elicitations, interaction-to-protected-entry
   relationships, prompt/operation admission, cancellation and abort state, invalidation, and terminal failure.
- Move the current interaction registry and role-operation coordinator behind each aggregate. Their member-local
   maps are keyed only by request or operation ID; their APIs accept no member or role argument. Remove the
   squad-wide keyed dictionaries and sets rather than wrapping them.
- Make provider-event projection operate only on the selected aggregate. Registering or completing an interaction,
   protecting or releasing its transcript entry, aborting, and terminal failure commit all related local changes
   under the aggregate's single mutation boundary.
- Expose immutable member snapshots containing the member identity, display name, genuine role metadata, state,
   usage, pending interactions, and transcript position. The member's provider-session association is reached through
   that aggregate rather than a parallel session map. The squad facade keeps only the ordered member directory,
   routing, and immutable snapshot composition; it exposes no mutable member objects.
- Rename internal application/transcript C# types to member terminology. Keep an explicit mapping to the unchanged
   version-5 UI, version-1 control, provider, handoff, and CLI contracts at their existing adapters; do not leak that
   legacy vocabulary back into the aggregate.
- Do not change or add tests. Use the unchanged interaction, abort, terminal-session, transcript-retention,
   readiness, handoff, and snapshot scenarios as regression gates for the refactoring.

**Acceptance:** The ordered member directory is the only application-domain collection keyed by member identity.
After routing, no aggregate, projector, interaction holder, transcript state, operation coordinator, or failure path
accepts or indexes by member/role. Snapshots are immutable and internally consistent. Existing transcript ordering,
retention, interaction restoration, abort retry, terminal finality, sibling-failure behavior, serialized messages,
and UI behavior are unchanged. No test source changes in this slice.

**Status: complete (a5290ee808).** Each member has one aggregate that owns projected status, provider-session
association, transcript, pending interactions, and operation/abort/failure. The ordered member directory is the only
application-domain collection keyed by member identity. Immutable member snapshots include identity, display name,
role metadata, state, usage, pending interactions, and transcript position. Architecture documentation describes
this ownership without claiming processor isolation or Headquarters/`Squad` replacement. Slice 3 is active.

### Slice 3 - Give every member an independent typed processor [done]

**Outcome:** Holding provider I/O for one member cannot delay commands, provider events, interactions, snapshots, or
transcript publication for another member, while same-member ordering and cancellation remain unchanged.

- Add one single-reader, bounded processor per member. Route typed prompt, harness, abort, interaction-response,
   provider-event, session-terminal, operation-completion, and retirement messages from the squad facade to that
   processor. Remove the shared `Channel<Func<Task>>` and opaque command delegates.
- Make the processor the aggregate's sole mutable accessor. A processor may start provider I/O but must not await it
   inside the message loop. Return success, failure, and cancellation as typed completion messages carrying the
   squad generation, member, and operation identity; reject completions that no longer match the active operation.
- Apply backpressure by asynchronously waiting for bounded mailbox capacity. Do not drop provider events, create an
   unbounded side queue, block a provider callback synchronously, or turn overload into a process-wide failure.
- Preserve prompt serialization, interaction restoration on failed responses, abort leader/follower coalescing,
   failed-abort barriers, event invalidation, accepted-command draining, and terminal-session rejection within one
   member. Retirement closes admission, cancels external operations, drains defined completions, and stops the
   processor before its session/runtime can be released.
- Compose squad snapshots from the latest immutable member snapshots. They need not be a globally atomic instant,
   but each member snapshot and transcript sequence must be self-consistent.
- Do not add concurrency or processor tests and do not adapt existing timing or ordering scenarios. The unchanged
   prompt-isolation, interaction, abort-ordering, transcript-ordering, terminal-session, and shutdown-admission
   features are the supported regression gate.

**Acceptance:** There is one processor and one mutation path per member, no shared application command queue, and no
cross-member ordering dependency. Blocked `coder-a` I/O cannot delay `coder-b`, including when both reference
`coder`; same-member commands still serialize and stale completions/events cannot reopen canceled or terminal work.
No test source changes in this slice, and every existing observable ordering remains unchanged.

**Status: complete (1d4dde5924).** Each member has a bounded single-reader processor that is the sole writer of its
aggregate. Typed prompt, harness, abort, interaction, event, terminal, starting, outcome, and retirement messages
replace the shared command channel. Completions carry generation, member, and operation identity and are dropped
when stale. Slice 4 remains pending until the architect activates it.

### Slice 4 - Separate Headquarters from the replaceable Squad generation

**Outcome:** Headquarters owns a process shell and one serialized active-squad slot, while one `Squad` owns and
retires every resource whose lifetime follows a generation through a single contract.

- Rename the process owner from `SquadApplication` to Headquarters terminology. Keep the project lease/control
   endpoint, window and UI transport, sleep inhibition, issue catalog, durable workspace services, transcript
   archive, and active-squad replacement authority at process lifetime.
- Introduce one `Squad` generation containing its immutable configuration snapshot and generation identity, role
   catalog, ordered member directory/processors, backend/runtime and sessions, command admission, observers,
   handoff-pump participation, and transient member state. Remove parallel ownership of those resources from
   Headquarters and the current runtime/view-model objects.
- Split one-time workspace/process preparation from generation preparation. A replacement reloads current
   configuration and role prompts and creates a new backend/member context without resetting worktrees or durable
   handoff queues; initial non-continued launch retains its current reset semantics.
- Give `Squad` one cohesive start operation and one idempotent, failure-collecting retirement operation. Close
   admission first, stop handoff participation, cancel and drain processors/observers, then retire the provider
   runtime. Retain any handle whose termination is uncertain and report an explicit non-conclusive result.
- Serialize initial installation, replacement, and final stop at Headquarters. Do not start a new generation until
   old retirement is conclusive; failed new startup leaves the slot empty and retryable, while uncertain old
   retirement keeps that generation owned and blocks retry. Shutdown uses the same gate.
- Make the process-lifetime UI/application port publish only the currently installed generation. Every command,
   provider event, readiness observation, external-operation completion, transcript mutation, and handoff wake-up
   carries or is captured with a strong generation identity and is rejected if retired. A member processor cannot
   outlive its `Squad`.
- Move transcript archive lifetime to Headquarters and give each generation/member a bounded handle that preserves
   history and monotonic publication identity without allowing a retired member to publish. Durable handoff storage
   remains workspace-owned; only polling and wake-up participation are generation-owned.
- Preserve the current supported launch/termination policy until the restart issue exposes replacement: a terminal
   backend or handoff-pump failure may still be selected as the process's primary failure. Do not add a test-only
   second-generation trigger. Do not change or add tests. Use the unchanged Headquarters lifecycle, early-shutdown,
   partial-startup, terminal-failure, cleanup-diagnostic, shutdown-admission, transcript-archive, delivery, and
   recovery scenarios to protect initial installation and complete retirement.

**Acceptance:** Headquarters has no backend, session, processor, interaction, handoff-pump, or live-member teardown
steps outside `Squad.RetireAsync` (exact method name may differ). At most one generation is owned; uncertain
retirement cannot overlap a new one; a failed replacement does not dispose process resources. Process shutdown
still reports primary and cleanup failures correctly and releases every externally observable resource. No
`SquadApplication` compatibility type remains. The later restart issue can invoke the replacement operation without
moving generation resources again. Headquarters control remains version 1, UI protocol remains version 5, Vue and
CLI remain unchanged, and no test source changes in this slice.

## Acceptance criteria

- `blaxquad/squad.json` schema version 2 represents reusable roles and uniquely identified members separately, gives
  every member one valid role, permits duplicate role references, and addresses the leader by member.
- Legacy one-entry-per-role configuration is rejected with the documented identity-preserving migration; it is not
  silently reinterpreted or retained as a compatibility shape.
- The domain model represents Headquarters, one squad generation, reusable roles, and uniquely identified squad
  members as distinct concepts.
- Headquarters owns process-lifetime resources and at most one replaceable active `Squad`; the active squad owns all
  generation-scoped runtime and member resources behind one retirement contract.
- Replacing the squad does not restart the Headquarters process, window, UI connection, control endpoint, project
  lease, or durable workspace services.
- Headquarters exposes one serialized squad-replacement operation for the restart and start-issue commands owned by
  the later restart issue; this issue adds no competing replacement path or presentation trigger.
- A failed or uncertain replacement never permits overlapping squad generations. It returns an explicit retryable
  or non-conclusive result to the future command boundary without disposing Headquarters resources.
- A supported configuration can define at least two members that reference the same role and role prompt.
- Duplicate member identities are rejected; duplicate role references are valid.
- Worktrees, agent settings, handoff mailboxes, live state, transcripts, interactions, operations, failures, and UI
  panels belong to members rather than roles. Provider sessions are member-addressed while their lifecycle remains
  owned by the backend runtime.
- Role definitions and prompts contain no mutable member runtime state and are not duplicated for same-role members.
- The application has one cohesive mutable owner per member for projected state, transcript, pending interactions,
  operation coordination, invalidation, and failure.
- The squad facade is limited to member routing, read-model composition, and squad-level commands; it is not the
  mutable aggregate for all members.
- Event projection mutates only the addressed member aggregate and has no dependency on a squad-wide mutable
  interaction registry.
- Same-role members have independent sessions and observable state; blocking, aborting, failing, or responding to an
  interaction for one does not affect the other.
- Existing guarantees for transcript ordering, same-member command serialization, cross-member concurrency,
  interaction routing, abort behavior, failure isolation, readiness, and shutdown remain observable.
- Existing projects have an explicit compatibility or migration path with no silent reassignment of members,
  prompts, worktrees, handoffs, or transcript history.
- Headquarters control protocol version 1, UI protocol version 5, handoff schema version 1, CLI output, Vue behavior,
  and their existing tests remain unchanged. Their role-shaped participant addresses are explicit compatibility
  mappings at the application boundary.
- Internal C# application/configuration code and architecture documentation use `role` and `member` according to the
  naming rules above without forcing that terminology onto unchanged public contracts.
- Test adaptations and new tests are limited to the version-2 configuration contract. Every non-configuration
  Gherkin and Playwright source remains unchanged and protects the refactoring as an existing regression suite.

## Non-goals

- Hosting multiple active squads concurrently in one Headquarters process.
- Dynamically adding or removing members while Headquarters is running.
- Sharing a provider session, transcript, interaction, or failure state between members that have the same role.
- Selecting an actor framework or eliminating every lock, semaphore, channel, task, or concurrent collection.
- Moving process lifecycle, backend ownership, UI delivery, handoff delivery, transcript storage, or shared-worktree
  scheduling into member aggregates.
- Implementing the shared-worktree scheduling behavior specified by issue 023.
- Redesigning the content of constitution or role prompts beyond resolving each member's referenced role prompt.
- Adding the restart button or issue-start presentation; the restart-button issue consumes the replacement operation
  established here.
- Renaming or versioning the existing CLI, provider SPI, Headquarters control protocol, UI protocol, handoff format,
  fake-provider control API, Gherkin vocabulary, Vue components, or dashboard presentation.
- Adding tests for internal aggregates, processors, concurrency, generation ownership, replacement internals, or
  other non-configuration implementation details.

## Slice 2 review (c078fc6e07) — addressed (a5290ee808)

### Finding 1 — Medium

- **Location:** `src/squad.Application/SquadViewModel.cs` (`mySessions`, `RegisterSession`, `TryCaptureSession`,
  `RunForRoleAsync`, `CancelAllPendingInteractionsAsync`).
- **Violated behavior:** Slice 2 acceptance requires the ordered member directory to be the only application-domain
  collection keyed by member identity, and requires the member's provider-session association to be reached through
  that aggregate rather than a parallel session map. After `GetMember`, `RunForRoleAsync` still indexes
  `mySessions` by the compatibility `role` string. Session lifecycle may remain backend-owned; the association may
  not.
- **Root cause:** `PendingInteractionRegistry` and `RoleOperationCoordinator` were moved into `MemberAggregate`, but
  the role-keyed `IAgentSession` dictionary was left on the facade as a second member-identity map.
- **Required outcome:** After routing, obtain the current session from the selected `MemberAggregate`. Remove
  `mySessions` as a member-keyed collection. Keep backend/runtime lifetime of session objects outside the aggregate.

### Finding 2 — Medium

- **Location:** `src/squad.Application/Members/MemberSnapshot.cs`; `MemberAggregate.CreateSnapshot`;
  `SquadViewModel.CreateSnapshot` / `CreateTranscriptSnapshot`; `InitializeRoles`;
  `src/squad.Workspaces/PreparedLaunch.cs` (`MemberNames` only).
- **Violated behavior:** Slice 2 requires immutable member snapshots that contain member identity, display name,
  genuine role metadata, state, usage, pending interactions, and transcript position, and that the facade compose
  read models from those snapshots without exposing mutable member objects. `MemberSnapshot` is a renamed
  `AgentRoleSnapshot` (status/usage only). The version-5 payload still reads live `Permissions`/`Inputs`/
  `Elicitations` and transcript state from the mutable aggregate. The aggregate never receives display name or the
  referenced role, so two members sharing `coder` cannot be snapshotted with genuine role metadata.
- **Root cause:** Ownership of interactions and transcript was moved into the aggregate, but snapshot composition
  and member metadata were left as they were when `AgentRoleState` held only projected agent fields.
- **Required outcome:** Produce one immutable member snapshot under the aggregate's mutation boundary with the
  fields above. Map the unchanged version-5 UI snapshot from those values. Thread configured member identity,
  display name, and role reference into the aggregate at initialization. Do not change tests or wire field names.

### Finding 3 — Medium

- **Location:** `docs/manual/architecture.md` (Responsibility boundaries: Application model; State ownership:
  "Role and interaction state"; Architectural characteristics item 2).
- **Violated behavior:** Each slice must update directly affected architecture documentation. Internal architecture
  docs must use `role` vs `member` per the naming rules. The application model is still described as one
  synchronization boundary that records pending interactions, coordinates per-role operations, and owns the
  role-session catalog together with projected role state. `docs/manual/modules.md` was updated; architecture was
  not.
- **Root cause:** The commit renamed application/transcript types and rewrote the module blurb, but left the C4
  responsibility and state-ownership narrative on the pre-slice ViewModel-as-aggregate model.
- **Required outcome:** Describe the ordered member directory, per-member aggregate ownership of projected state,
  transcript, pending interactions, and operation/abort/failure, and facade-only routing plus immutable snapshot
  composition. Keep public CLI/UI/control vocabulary unchanged. Do not claim slice 3 processor isolation or slice 4
  Headquarters/`Squad` replacement.

## Slice 3 review (cc2c93a618) — addressed (1d4dde5924)

### Finding 1 — Medium

- **Location:** `src/squad.Application/SquadViewModel.cs` (`myProcessors`, `InitializeRoles` closures,
  `DispatchPromptAsync`, `AbortRoleAndWaitAsync`/`AbortRoleAsync`, `CompleteInteractionCoreAsync`,
  `ApplyProjectedEvent`, `MarkRoleFailedCore`, `RegisterSession`, `CancelAllPendingInteractionsAsync`);
  `src/squad.Application/Members/MemberProcessor.cs`.
- **Violated behavior:** Slice 3 requires one processor as the aggregate's sole mutable accessor and one mutation
  path per member. Acceptance still requires the ordered member directory to be the only application-domain
  collection keyed by member identity. `MemberProcessor` never holds or writes the aggregate. All domain mutations
  remain in `SquadViewModel` methods invoked from constructor closures, including from detached tasks concurrent
  with the read loop (`MarkWaitingForResponse`, interaction remove/restore, abort idle/clear) and from the public
  caller (`TryBeginAbort`, `RegisterSession`, shutdown `ClearInteractions`). `myProcessors` is a second
  member-keyed map. `OperationOutcomeMessage` only resolves a `TaskCompletionSource`; it does not apply member
  state.
- **Root cause:** The shared `Channel<Func<Task>>` was replaced with a per-member mailbox that forwards back into
  the same ViewModel mutation methods, and the processor was stored beside the directory instead of with the member.
- **Required outcome:** Give each member one processor as the only writer of its aggregate. Start provider I/O
  without awaiting it on the read loop, but apply start and completion mutations on that loop. After routing, do
  not index another member-keyed collection. The facade may only admit, route, and compose immutable snapshots.

### Finding 2 — Medium

- **Location:** `src/squad.Application/Members/MemberMessage.cs` (`OperationOutcomeMessage`);
  `MemberProcessor.RunDetachedAsync` / `PostOutcomeAsync`.
- **Violated behavior:** Slice 3 requires typed completion messages that carry squad generation, member, and
  operation identity, and requires rejecting completions that no longer match the active operation. Acceptance:
  stale completions/events cannot reopen canceled or terminal work. `OperationOutcomeMessage` is an opaque `Action`
  with no identity. Completions are never matched or rejected. After retirement, `ChannelClosedException` applies
  that action off-loop. The commit message treats existing leases as a substitute for this contract.
- **Root cause:** Detached I/O reports only caller-task settlement. No generation/member/operation identity exists
  on the completion path, so there is nothing to reject.
- **Required outcome:** Return success, failure, and cancellation as typed completion messages carrying generation,
  member, and operation identity. Drop completions that no longer match the active operation (including after abort,
  terminal failure, or retirement). Do not apply opaque leftover actions off-loop. Generation may be a token for
  the current application lifetime until slice 4 introduces `Squad`; do not implement Headquarters/`Squad`
  replacement here.

### Finding 3 — Medium

- **Location:** `src/squad.Application/Members/MemberMessage.cs` (`SendPromptMessage.Operation`);
  `MemberProcessor` constructor `Func<...>` fields; `SquadViewModel.SendAsync` / `SendHarnessAsync`.
- **Violated behavior:** Slice 3 requires routing typed prompt, harness, abort, interaction-response,
  provider-event, session-terminal, operation-completion, and retirement messages, and removing opaque command
  delegates. `SendPromptMessage` still carries `Func<IAgentSession, CancellationToken, Task>`. The processor is
  constructed from seven ViewModel delegates that close over the compatibility `role` string. Prompt and harness
  remain one message distinguished only by that func.
- **Root cause:** The mailbox types wrap the previous command delegates instead of naming the member operations
  the processor applies to its aggregate.
- **Required outcome:** Make each routed operation a typed message the processor interprets against its member.
  Distinguish prompt and harness without an opaque operation delegate. Keep public CLI/UI/control contracts and
  tests unchanged.

## Slice 3 review (cf7f906994) — addressed (1d4dde5924)

This commit merges `origin/main` (transcript assembly fold) into the slice 3 branch. It does not change
`MemberProcessor`, `MemberMessage`, or the ViewModel mutation/routing design. Findings 1-3 from the cc2c93a618
review remain unresolved and still block acceptance.