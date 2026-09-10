---
title: Organize and simplify acceptance-test support
priority: 2
---

# Organize and simplify acceptance-test support

## Problem

`src/squad.Specs/Support` is a flat namespace containing 36 files with several unrelated responsibilities:

- scenario orchestration and workspace setup;
- child-process execution and diagnostics;
- the headless UI protocol client and transcript observations;
- fake and echo agent-provider implementations;
- the fake-provider control transport;
- handoff and task mailbox fixtures; and
- provider-selection fixtures.

This makes ownership hard to see and turns `Support` into a default destination for new test infrastructure. Several
large types are also likely to contain more than one independent concern, while small records, protocol handlers,
fixtures, and exceptions sit beside them without any visible grouping.

The directory currently defines three custom exception types. Their existence is not automatically a defect:
callers distinguish some of them and the timeout exceptions preserve useful failure diagnostics. Each exception
still needs an explicit reason to exist; a custom type that only renames a built-in exception or formats a message
adds maintenance cost without adding behavior.

This issue concerns the implementation structure of the acceptance-test support. The feature language and binding
organization are handled by [Build a reusable Gherkin step language](024%20build%20a%20reusable%20gherkin%20step%20language.md).

## Goal

Make the acceptance-test support easy to navigate, cohesive, and no larger than its responsibilities require.
Organize support by the external boundary it drives, remove dead or redundant helpers, and retain custom types only
when they provide distinct behavior, handling, or diagnostics.

The result must remain a behavior-preserving refactoring of the black-box suite. It must not change product behavior,
weaken scenario coverage, introduce production APIs for tests, or create production-shaped test assemblies.

## Scope

- Classify every file in `src/squad.Specs/Support` by one owning responsibility.
- Introduce responsibility-based folders and matching namespaces.
- Review the cohesion of the largest support types and extract only independently owned state, lifecycle, I/O,
	persistence, or protocol concerns.
- Review custom exceptions, accessibility, duplicate helpers, and unused support code from their call sites.
- Preserve one scenario-scoped owner for startup, teardown, and shared scenario state.

## Target structure

Use a small set of responsibility folders. The exact names may be adjusted during the inventory, but every folder
must have a clear owner and must not merely mirror production assemblies.

```text
Support/
	Scenarios/          scenario facade, role/command adapters, workspace lifecycle
	Processes/          child-process execution, command results, process diagnostics
	Ui/                 headless UI client and UI/transcript observations
	Agents/             fake/echo providers and provider-selection fixtures
		Control/          fake-provider control transport, protocol, handlers, and waits
	Mailboxes/          handoff and task setup/observation
```

Namespaces must follow these folders. Keep one top-level type per source file. A type used by only one owner may be
placed beside that owner instead of creating a one-file category.

This structure is an ownership map, not a mandate to add a layer around every file. Do not introduce pass-through
wrappers or interfaces with only one implementation merely to make the tree look uniform.

## Design constraints

### Keep the black-box boundary

All support remains in `squad.Specs` and continues to drive the real executables, protocols, filesystem, Git, and
handoff storage. Only the external agent provider remains fake. Do not add test hooks, callbacks, alternate product
behavior, `InternalsVisibleTo`, or new production abstractions for this cleanup.

### Split by responsibility, not size

File length identifies review candidates, not automatic extraction points. A split is justified when a concern has
its own state, lifecycle, I/O boundary, or reason to change. Keep related state and synchronization together even if
the resulting owner is substantial.

`BackendScenario` remains the scenario composition root. Extracted collaborators may own cohesive work, but must not
duplicate startup, teardown, mutable scenario state, or protocol reconciliation. Step definitions should continue to
use the scenario facade instead of assembling support objects independently.

### Keep failure diagnostics actionable

Review `FakeProviderControlProtocolException`, `FakeProviderControlTimeoutException`, and
`HeadlessUiWaitTimeoutException` from their throw, catch, and assertion sites.

Retain a custom exception only when at least one of these is true:

- a caller must distinguish that failure category;
- it carries structured diagnostic state or behavior not supplied by the base exception; or
- the type forms a deliberate test-support contract that makes a meaningful assertion clearer.

Otherwise use the appropriate built-in exception and a shared diagnostic formatter where needed. Removing a custom
exception must not discard captured process, protocol, UI, or provider state from the failure message.

### Remove proven redundancy

Use reference searches before deleting or merging support. Remove dead fixtures, handlers, records, aliases, and
duplicate formatting or waiting logic only after confirming that no scenario depends on their behavior. Preserve
bounded waits, deterministic acknowledgements, cancellation, cleanup, and failure diagnostics.

## Implementation plan

1. Inventory every support type, its callers, its state, and its external boundary. Record its target owner and mark
	 dead code, duplicate behavior, unnecessary public accessibility, and files containing multiple top-level types.
2. Establish the folder and namespace structure with mechanical moves first. Keep each migration slice compiling and
	 avoid mixing moves with behavioral rewrites.
3. Move scenario/workspace, process, UI, agent-provider/control, and mailbox support into their owners. Update
	 namespaces and imports, and split multiple top-level types into separate files.
4. Review the three custom exceptions against actual handling and diagnostic needs. Remove unjustified types,
	 consolidate repeated message construction, and keep distinct failure categories where callers rely on them.
5. Review the largest classes for independent responsibilities. Extract only cohesive owners, beginning with code
	 that has separate state or I/O lifecycle; leave orchestration in `BackendScenario`.
6. Remove support that is demonstrably unused or duplicates an established helper. Reduce public types and members
	 to the minimum required by Reqnroll bindings and the child-process provider-loading boundary.
7. Run the complete black-box specification suite after each responsibility is migrated. Fix namespace, lifecycle,
	 synchronization, and diagnostics regressions within that slice before continuing.
8. Update `docs/manual/test-strategy.md` only if the cleanup establishes a durable support-organization rule that
	 future acceptance tests need to follow.

## Acceptance criteria

- Every file under `src/squad.Specs/Support` belongs to a named responsibility folder, and its namespace matches its
	folder.
- The resulting folders separate scenario orchestration, process support, UI protocol support, agent-provider and
	control support, and mailbox support without mirroring the production project graph.
- `BackendScenario` remains the single scenario-scoped composition root and teardown owner; support is not assembled
	independently by individual step-definition classes.
- Every source file contains one top-level type, and types and members are public only when an assembly or framework
	boundary requires it.
- Each retained custom exception has a concrete caller or diagnostic reason that a built-in exception cannot express
	as clearly. Exceptions that only rename a built-in type or format a fixed message are removed.
- Timeout and protocol failures still report the relevant process output, protocol observations, UI state, and
	provider-control diagnostics needed to investigate a failed scenario.
- Large support types have been reviewed for independent concerns. Any extraction owns real state, lifecycle, I/O,
	or protocol behavior; no pass-through layers or one-implementation interfaces are introduced.
- Unused support and proven duplicate parsing, waiting, or diagnostic code are removed without changing deterministic
	synchronization or cleanup behavior.
- Feature wording and Gherkin binding vocabulary are unchanged by this issue except for namespace/import updates
	required by file moves.
- No production assembly or public product API changes solely to accommodate the test refactoring.
- The complete `squad.Specs` suite passes after the reorganization.

## Non-goals

- Redesigning the Gherkin vocabulary or reorganizing step definitions; that belongs to the reusable-step-language
	issue.
- Splitting `squad.Specs` into production-aligned test assemblies.
- Replacing real Git, filesystem, process, UI protocol, host-control, handoff, or task behavior with mocks.
- Changing product behavior, supported protocols, scenario semantics, or timing guarantees.
- Enforcing arbitrary file-size or line-count limits.

