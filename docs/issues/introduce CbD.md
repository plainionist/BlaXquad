---
title: introduce CbD
priority: 2
---

# Introduce Design by Contract

## Goal

Use the shared `System.Contract` utility from the `squad.Domain` assembly to make caller obligations and impossible
internal states explicit, fail close to the defect, and distinguish programming errors from expected operational
failures.

## Contract policy

Nullable reference types are compile-time annotations, not runtime validation. In this repository they are enforced
with warnings as errors, so explicit null guards on non-nullable parameters in trusted typed call paths are redundant
and should be removed. This does not remove validation at untyped boundaries: JSON, CLI arguments, handoff files,
reflection-loaded plug-ins, filesystem state, and process responses must continue to reject malformed input with
their existing boundary-specific diagnostics.

Use the contracts as follows:

- `Contract.Requires` expresses a semantic obligation placed on a caller that the type system cannot express, such
  as a positive timeout, a bounded priority, a valid roster relationship, or a supported typed variant.
- `Contract.Invariant` expresses a state that should be impossible after inputs have crossed their validation
  boundary, such as installing into a non-empty active-squad slot or attaching a runtime session twice.
- Expected lifecycle rejection, unavailable resources, cancellation, disposal, malformed external data, protocol
  errors, provider failures, process failures, and user mistakes remain normal control flow with their existing
  exception/result semantics. They are not invariants.
- Assert each rule at its narrowest authoritative boundary. Do not duplicate configuration, protocol, or handoff
  validation in downstream modules.
- Do not add null contracts for non-nullable reference parameters. Keep nullable parameters and deliberately lenient
  configuration models nullable where absence is supported.

`Contract` is declared in the `System` namespace and supplied by `squad.Domain`; every compiled module already has
the required project reference. No new dependency or `using` directive is needed.

## Audit boundary

Audit all hand-written C# under `src`, excluding generated `bin`/`obj` content and `squad.Specs`. Include the fake
provider and hosting assemblies because they are compiled and loaded through the same adapter contracts, but do not
treat public members on their internal test-fixture types as supported product API.

For preconditions, inspect every externally visible constructor and method plus public/internal component entry
points whose implementations assume more than their parameter types guarantee. For invariants, inspect every
non-trivial stateful class, but add a check only where it protects a stable ownership, identity, ordering, capacity,
or lifecycle rule. Stateless mappings and thin adapters need no ceremonial invariant.

## Implementation plan

### Slice 1: Remove redundant non-null guards [done]

1. Remove the existing `ArgumentNullException.ThrowIfNull` calls for non-nullable parameters from
   `AgentEventChannel`, `CopilotSdkAgentRuntime`, `SquadMembers`, `SquadViewModel`, `Headquarters`, and
   `HeadquartersLease`.
2. Do not replace those calls with `Contract.RequiresNotNull`; nullable annotations and warnings-as-errors own these
   trusted typed call paths.
3. Leave nullable-value handling and validation of deserialized, reflected, filesystem, process, and protocol input
   unchanged.

This is a behavior-preserving mechanical slice. It must build independently and must not contain any new
precondition or invariant decisions.

**Status: complete (5265da2d4a).** Removed `ArgumentNullException.ThrowIfNull` for non-nullable parameters on those
six trusted typed call paths. No `Contract` replacements were added. Untyped-boundary validation is unchanged.
Slices 2–6 remain pending until the architect activates the next slice.

### Slice 2: Standardize existing semantic preconditions [done]

1. Replace the existing positive/non-negative range guards in `SquadViewModel` and
   `HeadquartersControlClient.WaitForAgentAsync` with `Contract.Requires`.
2. Express the non-empty sleep-inhibitor command and the `UiProtocolSession` requirement that its UI also implement
   `ITranscriptUi` as caller preconditions.
3. Classify the supported interaction-request variants in `CopilotSdkAgentSession` as a caller precondition.
4. Leave enum exhaustiveness failures and expected object lifecycle failures for the invariant audit; do not mix
   them into this slice.
5. Extend the `squad.Domain` description in `docs/Manual/modules.md` with the `Requires`/`Invariant` distinction and
   the rule against redundant non-null guards.

**Status: complete (e1acae79a8).** Existing range, capability, command-emptiness, and interaction-request-variant
guards now use `Contract.Requires`. Enum exhaustiveness and lifecycle failures were left for later slices.
`docs/Manual/modules.md` records the Requires/Invariant distinction and the rule against redundant non-null
guards. Slices 3–6 remain pending until the architect activates the next slice.

### Slice 3: Add foundational API preconditions

Audit `squad.Domain`, `squad.Configuration`, `squad.Workspaces`, `squad.Handoffs`, `squad.Process`, and
`squad.HeadquarterTools`, then add only semantic checks whose absence currently permits an invalid object or a later,
less diagnostic failure. At minimum:

1. Ensure a launch `SquadDefinition` has members, unique member identities, and a leader contained in that roster.
2. Require `Priority.Parse` to receive the documented two-digit representation and `Priority.Format` to receive a
   value from 0 through 99.
3. Require a `CliExitException` to carry a non-zero exit code and process-launch APIs to receive a non-blank
   executable name.
4. Require `ProjectLayout.Create` to receive a non-blank working directory.
5. Preserve `SquadConfig`'s documented lenient read behavior and `HandoffDocument.Validate`'s external-data
   diagnostics rather than duplicating those rules in domain constructors.

### Slice 4: Add orchestration and protocol API preconditions

Audit `squad.AgentProvider.*`, `squad.Hosting.*`, `squad.Application`, `squad.Runtime`, `squad.Ui.Abstractions`, and
`squad.Ui.Protocol`, then add checks at the component that owns each assumption. At minimum:

1. Require every supplied timeout, publication interval, page size, retention capacity, and announcement capacity
   that drives a bounded operation to be positive; require indexes and sequence positions to be non-negative and
   ordered where applicable.
2. Require transcript retention limits to be internally compatible, including each per-entry bound not exceeding
   its corresponding total bound.
3. Reject provider session registration for an unknown member or a second live session for the same member at the
   provider callback boundary.
4. Validate typed transcript update and announcement shapes before code relies on null-forgiving access to their
   kind-specific fields.
5. Keep malformed UI messages, invalid handoff documents, plug-in load failures, and provider/process failures on
   their existing validation and reporting paths; they are not `Contract` failures.

### Slice 5: Protect orchestration lifecycle invariants

Audit the stateful lifecycle owners in `squad.Runtime`, `squad.AgentProvider.CopilotSdk`, `squad.Handoffs.Delivery`,
and the hosting adapters. Add cheap invariant checks at mutation boundaries, including:

1. the Headquarters active-squad slot is empty before installation;
2. a `SessionGeneration` and `Squad` cannot start after starting or retirement has begun;
3. a Copilot SDK session is attached exactly once and runtime-dependent operations only occur after attachment;
4. an `OperationLease` registers at most one operation before disposal and an `AbortLease` reaches exactly one
   terminal outcome before release; and
5. paired lifecycle state such as a polling task and its cancellation source cannot diverge.

Preserve explicitly idempotent start/stop/dispose paths and expected "not started", "shutting down", unavailable,
and teardown-failure behavior. Do not turn those states into invariants.

### Slice 6: Protect application and transcript invariants

Audit the stateful owners in `squad.Application` and `squad.Ui.Protocol`. Add inexpensive checks that protect the
documented authoritative state model, including:

1. member identity, configured order, and leader lookup remain consistent;
2. transcript streaming buffers and their entry indexes are either both present or both absent;
3. retained character counts never become negative, active tool-call identities are unique, and protected
   transcript entries refer to known entries;
4. transcript journal character counts agree with retained announcements and sequence ranges remain monotonic; and
5. publication and synchronization positions never move backward.

Place checks at natural locked mutation boundaries. Do not add full-collection scans to hot paths when an equivalent
constant-time condition can protect the transition. Provider-originated duplicate or unknown interaction IDs and
client-originated invalid commands remain boundary failures, not internal invariants.

## Acceptance criteria

- No explicit null guard remains for a non-nullable parameter on a trusted typed call path in the audited scope.
- Every public/component API in scope has been reviewed; each additional semantic restriction is expressed once with
  `Contract.Requires`, and APIs without an extra semantic domain remain unchanged.
- Every non-trivial stateful class in scope has been reviewed; `Contract.Invariant` is added only for a stable,
  impossible internal state that can be checked without changing supported behavior.
- Existing operational and external-input failures retain their current user-facing diagnostics and exception/result
  semantics.
- The lenient role-command configuration reader remains lenient, and nullable domain values that represent supported
  absence remain nullable.
- No new project dependency, custom exception, interface default implementation, or frontend validation rule is
  introduced.
- Each slice builds and passes the existing black-box Gherkin suite. Add or change a Gherkin scenario only if a
  supported observable behavior changes; do not expose product internals merely to test a contract assertion.

## Non-goals

- Treating nullable reference annotations as runtime validation for untyped input.
- Replacing every `ArgumentException` or `InvalidOperationException` mechanically.
- Using `Contract.Invariant` for expected failures or recoverable lifecycle states.
- Redesigning the domain model, protocols, error taxonomy, or test architecture.
- Adding frontend contracts or duplicating authoritative C# rules in Vue.
