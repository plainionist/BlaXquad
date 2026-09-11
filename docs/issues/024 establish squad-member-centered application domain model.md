---
title: Establish a squad-member-centered application domain model
priority: 30
---

# Establish a squad-member-centered application domain model

## Decision

Treat the domain-model restructuring and the role-to-squad-member rename as one change.

They cannot be implemented independently without creating throwaway work. The previous role-centered proposal would
first consolidate mutable state under a role identity, while the naming proposal would later split that same identity
into a reusable role and an operational squad member. It would also rename the same configuration, protocol, state,
and UI surfaces twice.

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

Names below define domain meaning. Exact source type names may be chosen during implementation, but code, protocol,
configuration, tests, and UI must use each term consistently once migrated.

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

A squad snapshot composes immutable member snapshots and carries the squad generation. It need not represent one
globally atomic instant across independent members. Headquarters publishes the active-squad phase and snapshot over
its process-lifetime UI connection. UI delivery may order and coalesce publications without becoming another
authoritative owner of member state.

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
- Use `squad member` or `member` for a configured participant and live agent-facing state.
- Use `role` only for the reusable responsibility and role prompt.
- Use `agent session` for the provider-owned live conversation/execution resource used by one member.
- Use `Headquarters` for the process-lifetime shell and `Squad` for the replaceable active generation; a process owner
  must not be named `SquadApplication` in the target model.
- Member names are unique within a squad; role names do not identify a unique member.
- A field, parameter, map key, route, or component named `role` must not carry a member identity.
- Leader selection, worktree ownership, session routing, handoffs, transcripts, interactions, and UI panels are
  member-addressed.
- Role prompts remain role-addressed and may be shared by multiple members.

This is a semantic migration, not a global textual replacement. For example, `rolePrompt` remains correct, while a
component that renders one participant should become a member component.

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

### External contract migration

- Bump the Headquarters control protocol from version 1 to version 2 when its `role` request/response field becomes
   `member`. Keep `.blaxquad/host.lock`, `.blaxquad/host.json`, its metadata version, the pipe identity, and the
   `agent-status` command name unchanged; none of those carries the conflated identity.
- Bump the UI protocol once, from version 5 to version 6, after generation ownership exists. Version 6 uses
   `member`/`members`, carries genuine role metadata separately, and carries the squad generation on every
   generation-bound command and publication. Do not retain version-5 aliases.
- Keep handoff schema version 1. Its persisted address fields are already the neutral `from`, `to`, and `recipient`.
   The version-2 configuration migration preserves old participant names as member names, so existing JSON handoffs
   remain unambiguous. Rename code, help, diagnostics, and tests that currently describe those values as roles.
- The fake-provider control pipe is private test infrastructure, not a compatibility contract. Migrate it directly
   to member vocabulary with its production provider boundary.

### Related-issue boundary

Issue 024 supplies the serialized active-squad replacement operation but adds no restart or start-issue command,
acknowledgement, button, or issue-explorer behavior. The `restart active squad` issue must expose this exact operation
through supported protocol and presentation boundaries and owns the first black-box scenarios that invoke a second
generation in one process. Do not add a direct-product-object test or test-only replacement trigger here.

Issue 027 must not run concurrently with this work. It is a later mechanical assembly consolidation and must consume
the resulting `Headquarters`/`Squad` ownership and version-2 control types without restoring `SquadApplication`,
role-addressed control fields, or compatibility wrappers. Issue 024 does not perform that project move.

## Implementation plan

Exactly one slice is active at a time. Each slice includes its production changes, black-box acceptance coverage,
obsolete test support, and directly affected manual/README updates. Do not defer a broad documentation or rename pass
that leaves the completed slice's supported behavior described with the wrong domain term.

### Slice 1 - Configure and launch reusable roles with distinct members [in progress]

**Outcome:** A configuration author can launch `coder-a` and `coder-b` as separate members that use the same `coder`
role prompt while retaining independent worktrees, settings, provider sessions, leader addressing, and startup
state.

- Implement the version-2 schema above with separate immutable role definitions and member configurations. Validate
   the schema version, unique role and member names, role-prompt existence, valid member role references, member
   worktree safety/uniqueness, agent settings, receive modes, and a leader member. Duplicate role references are
   deliberately valid.
- Replace role-shaped workspace rows and backend contexts with member-shaped values that include both member and
   role identity. Workspace creation, initial instructions, provider runtime creation, session registration, and
   event routing use member identity; only prompt lookup uses role identity. `IAgentSession` exposes its member, not
   a property named `Role`.
- Make provider interaction events session-local rather than embedding a second role/member address. The session
   boundary supplies the member exactly once when routing the event.
- Migrate `blaxquad/squad.json`, configuration examples, workspace/spec builders, and the production and fake
   provider adapters. The command-side configuration reader must understand version-2 members so existing CLI and
   handoff behavior remains functional until its terminology is migrated in Slice 2; do not retain legacy
   configuration support to achieve that.
- Add black-box configuration scenarios through the published Headquarters process for two same-role members,
   duplicate member rejection, duplicate role-reference acceptance, unknown role references, leader-member
   validation/defaulting, missing prompts, and the explicit legacy-schema diagnostic. Observe separate provider
   sessions and the shared role prompt through the fake provider boundary, not product objects.

**Acceptance:** The repository and generated test workspaces use only schema version 2. `coder-a` and `coder-b` can
start concurrently with different session IDs and worktrees, both receive the instruction for
`blaxquad/roles/coder.prompt`, and neither requires a `coder-a.prompt` or `coder-b.prompt`. Invalid configuration
starts no member session and reports the specific configuration diagnostic. Existing one-member-per-role lifecycle,
prompt, handoff, and CLI scenarios remain green against the new schema.

### Slice 2 - Address commands, readiness, and handoffs by member

**Outcome:** An operator or agent addresses one configured member consistently through `squad`, Headquarters
control, and durable handoff delivery, while `role` reports only the member's reusable responsibility.

- Replace `RoleRow`, `CurrentRoleResolver`, role-keyed command helpers, `IRoleNotifier`, and corresponding delivery
   vocabulary with member equivalents. Worktree context resolves exactly one member. Shared-worktree ambiguity
   remains rejected until issue 023.
- Make `squad context` report both `Member` and `Role`; add `--field member`, make `--field role` return the genuine
   role, and publish `member`, `role`, and `memberWorktreeRoot` in JSON. Remove `roleWorktreeRoot` and any alias that
   still means member.
- Make handoff help, validation, diagnostics, queue support, delivery maps, recovery, and wake-ups member-addressed.
   Preserve the version-1 `from`/`to`/`recipient` JSON and existing durable queue semantics.
- Migrate `squad-hq wait-for-agent` to accept a member and move the local control request/response to protocol
   version 2 with a `member` field and `unknown-member` result. Keep the persisted host metadata format and endpoint
   discovery stable.
- Update the constitution's handoff syntax and the role prompts wherever they identify an operational recipient;
   retain `role` where it means responsibility or role prompt.
- Recast the Context, Handoffs, Delivery, Recovery, task/batch queue, readiness, and control-protocol Gherkin
   vocabulary around members. Add a same-role scenario proving `coder-a` can hand off only to `coder-b`, whose
   mailbox and session receive the delivery independently.

**Acceptance:** No CLI, Headquarters-control, workspace, or delivery API named `role` carries a member identity.
Context exposes the member and its role separately; readiness and handoffs select members; same-role members have
independent queues and wake-ups. Version-1 handoff artifacts produced before the configuration migration remain
readable when the migrated member names are preserved.

### Slice 3 - Make one aggregate own each member's mutable state

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
- Rename application/transcript C# types to member terminology now. Until the version-6 cutover, the version-5 UI
   adapter may explicitly map those member values to its legacy wire fields; do not let that compatibility mapping
   leak back into the domain.
- Extend black-box interaction, abort, terminal-session, transcript-retention, and snapshot scenarios with
   `coder-a`/`coder-b` sharing `coder`: identical request IDs, abort, failure, and transcript eviction/protection for
   one member must leave the other unchanged.

**Acceptance:** The ordered member directory is the only application-domain collection keyed by member identity.
After routing, no aggregate, projector, interaction holder, transcript state, operation coordinator, or failure path
accepts or indexes by member/role. Snapshots are immutable and internally consistent. Existing transcript ordering,
retention, interaction restoration, abort retry, terminal finality, and sibling-failure behavior remain observable.

### Slice 4 - Give every member an independent typed processor

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
- Add a black-box scenario that holds a real provider interaction response or abort for `coder-a` while `coder-b`
   receives and publishes events and a prompt. Retain the existing same-member prompt/abort ordering and shutdown
   scenarios as focused gates; use provider acknowledgements, never sleeps.

**Acceptance:** There is one processor and one mutation path per member, no shared application command queue, and no
cross-member ordering dependency. Blocked `coder-a` I/O cannot delay `coder-b`, including when both reference
`coder`; same-member commands still serialize and stale completions/events cannot reopen canceled or terminal work.

### Slice 5 - Separate Headquarters from the replaceable Squad generation

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
   second-generation trigger. Use the existing Headquarters lifecycle, early-shutdown, partial-startup, terminal
   failure, cleanup-diagnostic, shutdown-admission, transcript-archive, delivery, and recovery scenarios to protect
   initial installation and complete retirement.

**Acceptance:** Headquarters has no backend, session, processor, interaction, handoff-pump, or live-member teardown
steps outside `Squad.RetireAsync` (exact method name may differ). At most one generation is owned; uncertain
retirement cannot overlap a new one; a failed replacement does not dispose process resources. Process shutdown
still reports primary and cleanup failures correctly and releases every externally observable resource. No
`SquadApplication` compatibility type remains. The later restart issue can invoke the replacement operation without
moving generation resources again.

### Slice 6 - Publish the member model through protocol and dashboard

**Outcome:** UI-protocol clients and dashboard users see ordered squad members, their reusable roles, and their squad
generation explicitly; every action and transcript update targets one member.

- Cut the UI wire contract directly from version 5 to version 6. Use `member` in command envelopes and
   interaction/transcript payloads, `members` in state and synchronization collections, and a member-valued `leader`.
   Include display name and genuine `role` metadata in each member summary. Add the squad generation to state,
   member-bound commands, transcript synchronization/updates/pages/entries, and any acknowledgement introduced by a
   later consumer; reject missing, stale, or mismatched generations server-side.
- Rename `RoleTranscript*`, role-addressed UI abstractions, protocol DTOs, synchronization positions, journals, and
   headless-client support to member terminology. Do not retain version-5 parsers, `role.abort`, `role` envelope
   fields, or aliases whose value is a member.
- Rename Vue `RoleState`, role caches/composables, `RolePanel`, and `RoleHeader` to member concepts. Key transient
   state by member, render one independently operable panel per same-role member, show the display name as primary
   identity and role as metadata, and keep authoritative validation/lifecycle rules in C#.
- Migrate all backend Gherkin phrases and Playwright fixtures to member language where they address a participant.
   Retain role language only for role definitions, role prompts, and assertions that two members share a role.
   Remove obsolete bindings immediately.
- Update the glossary, architecture, module inventory, test strategy, README, CLI help, example configuration, and
   all source comments touched by the model. Search C#, TypeScript/Vue, JSON, Gherkin, prompts, and stable
   documentation for every remaining `role`; each occurrence must describe a genuine role.
- Add focused Playwright coverage for two `coder` members rendering separate panels and preserving independent
   prompt drafts, focus/abort behavior, interactions, transcript reconciliation, history, status, and failures.
   Update raw protocol validation/order coverage for version 6 and generation mismatch rejection.

**Acceptance:** Protocol version 6 and the Vue model contain no role-named address. Same-role members render and
operate independently, while their shared role remains visible metadata. C# rejects stale-generation UI work; Vue
does not infer authoritative lifecycle state. All obsolete version-5, role-addressed binding, component, and DTO
surfaces are removed, and the full backend Gherkin and focused Playwright suites cover the final public vocabulary.

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
- Headquarters control protocol version 2 and UI protocol version 6 address members explicitly. Handoff schema
  version 1 remains compatible because its address fields are already neutral.
- Code, configuration, CLI output, protocol fields, UI components, tests, and stable documentation use `role` and
  `member` according to the naming rules above.
- Behavior changes are covered through the existing black-box Gherkin acceptance suite, with focused Playwright
  coverage for frontend behavior.

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