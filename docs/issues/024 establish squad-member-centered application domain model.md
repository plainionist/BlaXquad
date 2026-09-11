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

## Rough implementation plan

### Slice 1: Specify identities, lifetimes, and compatibility

1. Add black-box Gherkin scenarios for a squad containing `coder-a` and `coder-b`, both using the `coder` role prompt,
   and prove that commands, sessions, status, transcripts, interactions, handoffs, and failures remain independently
   addressed.
2. Characterize the observable boundary between Headquarters and its active squad: process resources survive squad
  replacement, old-generation work is rejected, durable history/work survives, and a failed replacement leaves
  Headquarters usable.
3. Introduce explicit role-definition and squad-member configuration concepts. Decide the concrete JSON shape and a
   controlled compatibility or migration path for existing one-entry-per-role configurations.
4. Make leader and recipient references resolve to member identities. Validate unique member names and valid role
   references while allowing duplicate role references.
5. Update the configuration model and workspace preparation so worktrees and agent settings belong to members and
   role prompts are resolved from the referenced roles.

### Slice 2: Establish the replaceable squad boundary

1. Introduce a process-lifetime Headquarters owner and one active-squad slot with serialized start, replace, and stop
  operations.
2. Make one `Squad` own the backend runtime, session catalog, generation admission, event observers, handoff-pump
  participation, member processors, and transient state for that generation.
3. Give `Squad` one cohesive start contract and one idempotent retirement contract that preserves uncertain backend
  ownership instead of clearing handles after attempted cleanup.
4. Keep the window, UI transport, Headquarters control endpoint, project lease, issue catalog, durable handoff state,
  transcript archive, and workspace lifetime outside the active squad.
5. Prove through the black-box suite that Headquarters can replace a squad, reject stale-generation work, recover
  durable work, survive replacement failure, retry safely, and later shut down normally.

### Slice 3: Establish the member aggregate

1. Introduce one member aggregate around the existing projected state and transcript behavior.
2. Move pending interactions and their transcript protection into that aggregate, keyed only by request ID.
3. Move per-member prompt admission, active cancellation, abort, invalidation, and failure state out of squad-wide
   role-keyed collections.
4. Expose immutable member snapshots rather than mutable state objects and preserve existing transcript retention and
   publication ordering.

### Slice 4: Establish independent member processors

1. Route typed commands and provider events from the squad facade to one processor by member identity.
2. Replace the shared opaque `Func<Task>` command queue with explicit member messages and completion messages for
   external provider operations.
3. Preserve same-member ordering while proving that a blocked member does not delay another member, including two
   members with the same role.
4. Keep squad-generation checks and genuinely cross-member coordinators at the squad boundary; keep only replacement
  coordination and process resources at Headquarters.

### Slice 5: Complete the semantic rename

1. Rename C# application, configuration, workspace, handoff, transcript, and protocol concepts according to their
   domain meaning. Do not rename genuine role-definition or role-prompt concepts.
2. Version or migrate persisted and wire contracts where renaming an operational `role` field to a member field is
   externally observable. Do not silently reinterpret ambiguous data.
3. Rename Vue state and participant components such as role panels and headers to member terminology, while keeping
   role labels available as member metadata.
4. Remove compatibility aliases after all callers use the explicit model; no API named `role` may continue to carry
   a member identity in the target state.

### Slice 6: Document and verify the model

1. Update the manual glossary, architecture, module descriptions, example configuration, and role-related CLI help.
2. Update existing acceptance scenarios to use member terminology where they address participants and role
   terminology where they select prompts or responsibilities.
3. Run the complete black-box Gherkin suite and focused Playwright coverage for member panels, routing, interactions,
   transcript updates, and same-role member independence.

## Acceptance criteria

- The configuration and domain model represent Headquarters, one squad, reusable roles, and uniquely identified squad
  members as distinct concepts.
- Headquarters owns process-lifetime resources and at most one replaceable active `Squad`; the active squad owns all
  generation-scoped runtime and member resources behind one retirement contract.
- Replacing the squad does not restart the Headquarters process, window, UI connection, control endpoint, project
  lease, or durable workspace services.
- Explicit restart and starting a selected issue use one serialized squad-replacement operation, and issue work cannot
  reach the retired generation.
- A failed or uncertain replacement never permits overlapping squad generations and leaves Headquarters in an
  observable, retryable state when retirement is confirmed.
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