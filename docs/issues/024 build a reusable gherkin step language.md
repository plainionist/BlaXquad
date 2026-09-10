---
title: Build a reusable Gherkin step language
priority: 3
---

# Build a reusable Gherkin step language

## Problem

The acceptance suite has grown by adding wording for each new feature instead of extending a small language of
reusable domain operations. The scenarios still cross the correct black-box boundaries, but many of their steps
describe the test driver that performs an action rather than the actor, action, or observable outcome being
specified.

Gherkin is also user-facing specification. A feature should be understandable to the user for whom the behavior is
public without requiring knowledge of the test implementation. `BackendScenario` has no meaning to that user;
Headquarters, an agent, a configured model, `squad`, `squad-hq`, `blaxquad/squad.json`, a transcript, and a handoff
do.

A refreshed baseline scan of `src/squad.Specs/Features/*.feature` and
`src/squad.Specs/StepDefinitions/*.cs` on 2026-09-10 found:

- 45 authored feature files containing 146 scenarios and 1,254 step occurrences;
- 340 binding attributes across 17 step-definition files;
- 150 bindings, 44% of the suite, in `BackendScenarioSteps` alone;
- 621 step occurrences, 50% of all feature steps, that literally mention `backend scenario`;
- 343 distinct phrases after quoted and numeric example values are normalized; and
- 174 normalized phrases, 51% of the vocabulary, that occur only once.

The raw number of steps is not itself the defect. The problem is that equivalent concepts have separate dialects,
data variation is encoded in prose and separate bindings, and scenario choreography is hidden inside one-off steps.
That makes a new scenario more likely to invent another phrase than compose existing ones.

### Test infrastructure has become the actor

Twenty-nine feature files use `backend scenario` in their wording. The most repeated phrases include:

- `the backend scenario observes a session started for role ... across the control pipe` (54 occurrences);
- `the backend scenario has enabled the fake-provider control transport` (48 occurrences);
- `the backend scenario starts squad-hq with the fake provider fixture` (47 occurrences);
- `the backend scenario observes an exit code of zero` (38 occurrences); and
- `the backend scenario requests a host-control shutdown` (32 occurrences).

`BackendScenario` is a useful test-owned facade, but it is an implementation detail of the bindings. In use-case
features the actors are the user, an agent, headquarters, the CLI, a project, or a transcript. The facade, fixture,
and control pipe should not define the language merely because they implement it.

Technical language is appropriate when it names a supported public surface. An operator knows the `squad` and
`squad-hq` executables, a configuration author knows the checked-in configuration and model settings, and an
integrator may know a documented UI protocol or agent-provider SPI. A fake-provider fixture, its private control
pipe, and the `BackendScenario` driver are test implementation details, not public technical language. Features
whose subject is only that test infrastructure must be recast around the public behavior it enables or removed.

### The same concepts have several dialects

Project and role setup is expressed as, among other variants:

- `a backend scenario configured with a ... role`;
- `a backend scenario configured with roles ...`;
- `a configured project with role ...`;
- `a configured project with roles ...`;
- `a Git project with context roles ...`;
- `a git project prepared with a ... role using the fake provider fixture`; and
- `a role-interaction scenario configured with roles ...`.

Sending a prompt is attributed variously to `the backend scenario`, `the role-interaction scenario`, `the ui`, or
`the ui client`. Receiving one is `observes`, `has received`, `has eventually received`, or `has not observed`, with
different bindings for timing and negation. Transcript observation, process startup, session availability, and
shutdown have similar parallel vocabularies.

These differences mostly identify which support class was introduced with a feature, not different product
semantics.

### Data variation is encoded as new prose

Several binding families differ only in one value or optional field:

- singular and plural role setup;
- echo versus fake provider, cancellable startup, and startup without the ready handshake;
- assistant, reasoning, system, tool, readiness, and usage events, including separate count and content-length
  variants;
- permission, input, and elicitation requests and responses with optional choices, URLs, form values, and
  string-encoded booleans; and
- transcript observations with different combinations of source, operation, content, state, and entry identity.

The suite already demonstrates that structured tables work well for queue contents and transcript entries, but only
14 feature files use tables and only two use scenario outlines. Other features encode lists as comma-separated
strings, booleans as strings, or every supported field combination as a new sentence.

### Some steps encode an entire scenario transition

Steps such as `requests a host-control shutdown as soon as it is reachable while sending the prompt ...` combine
actors, concurrency, timing, and two commands in one binding. They are difficult to reuse in the next ordering
scenario. Exact race orchestration may remain an atomic driver operation, but the feature language should express a
reusable ordering or concurrency relationship rather than the history of one scenario.

## Goal

Create a coherent Gherkin language from which new black-box scenarios can be composed without adding a binding for
each scenario. Every feature must speak from the perspective of a user of the behavior and use terms from the
product's public vocabulary. The language should carry variation as typed data and keep the test-owned facade behind
the bindings.

This is a behavior-preserving test refactoring. It must not change product behavior, weaken assertions, move
authoritative rules into tests, or introduce production APIs for test convenience.

## Required design

### Write every feature from a user perspective

Choose the user whose behavior is being specified, then write with concepts that user can know:

- an operator knows the squad, Headquarters, the dashboard, roles, agents, models, worktrees, transcripts,
  interactions, and startup or shutdown;
- a role's agent knows its role context, worktree, prompts, and the public `squad` commands for handoffs and queued
  work;
- a configuration author knows `blaxquad/squad.json`, the constitution and role prompts, and documented
  configuration fields such as role, model, permissions, worktree, and receive mode; and
- an integration user knows the documented UI protocol, host-control commands, or agent-provider SPI that they
  consume or implement.

Use the exact names established by the manual, CLI help, configuration schema, visible dashboard, and supported
protocols. A term is not user language merely because it is technically precise or names a C# type. Internal
coordinators, scenario facades, fake implementations, fixtures, private pipes, recording objects, and test timing
mechanisms stay behind the bindings.

The perspective may be technical. A UI-protocol feature can name public envelope types and fields because its user
is a protocol client. It must still describe what that client sends or receives, not how the test harness records
the exchange. Likewise, provider specifications may use the public provider contract without naming the fake
provider used to exercise it.

### Define the vocabulary before migrating it

Inventory the current bindings by semantic capability rather than by feature or support class. Derive canonical
terms from `docs/manual/glossary.md`, the rest of the manual, CLI help, the public configuration shape, and documented
protocols. Define language modules for these cohesive areas:

- project, role, worktree, model, and prompt configuration;
- Headquarters lifecycle and the public `squad-hq` commands;
- dashboard actions and role-directed prompts;
- role-local `squad` commands;
- agent sessions, replies, failures, and operation control;
- pending user interactions and responses;
- transcript events, synchronization, paging, and archival;
- handoffs, delivery, and task queues; and
- documented UI-protocol and agent-provider contract behavior.

Use one actor name and one verb for each meaning. A scenario facade may implement steps from several vocabularies,
but its type name must not become a second actor. Keep genuinely different semantics separate even when their code
could share a helper.

The canonical vocabulary and the rules below belong in `docs/manual/test-strategy.md` so future scenarios extend the
language instead of restarting it.

### Choose the right form of variation

Use the smallest form that preserves readable behavior:

- use a typed parameter when one scalar value varies;
- use a data table for collections, records with optional fields, ordered event streams, or groups of related
  observations;
- use a scenario outline when the same behavior is exercised for a matrix of examples; and
- use separate steps when the behavior or externally observable meaning is actually different.

Replace comma-separated lists and string-encoded booleans where a table or typed conversion communicates the shape.
Do not replace many narrow phrases with one opaque mega-step whose generic table is a programming language in
disguise.

Candidate forms to validate during the migration include:

```gherkin
Given `blaxquad/squad.json` configures:
  | role     | model     | receive mode |
  | coder    | gpt-5     | task         |
  | reviewer | gpt-5     | batch        |

When the user launches Headquarters with `squad-hq launch`
Then Headquarters starts agent sessions for:
  | role     |
  | coder    |
  | reviewer |

When the user sends "Review the change" to "reviewer"
Then the "reviewer" agent receives "Review the change"

When the "coder" agent emits these events:
  | type            | content     |
  | assistant-delta | Hello       |
  | assistant-delta | world       |
  | assistant       | Hello world |

Then the transcript for "coder" publishes:
  | operation      | source    | content     |
  | append-entry   | assistant | Hello       |
  | append-content |            | world       |
  | replace        | assistant | Hello world |
```

These examples show direction, not mandatory final wording. The implemented vocabulary must fit all existing
scenarios without losing distinctions such as incremental versus synchronized transcript state.

### Make steps composable

Each step should establish one meaningful precondition, perform one actor action, or observe one outcome from the
chosen user's perspective. Repeated environment preparation may be one semantic `Given`; it does not need to expose
every process and pipe operation.

For concurrent and failure scenarios, introduce reusable concepts such as a pending operation, an independently
started command, a released operation, or an action performed while another action is pending. Do not create a new
sentence that hard-codes every pair of concurrent actions. Synchronization must remain deterministic and use the
existing observable acknowledgements and bounded waits.

Use `Background` for readable state shared by scenarios. Keep fixture selection in test setup when it is not part of
the behavior under specification. Do not hide a contract-relevant precondition in a hook.

### Organize bindings by language domain

Split the responsibilities currently collected in `BackendScenarioSteps` into cohesive binding classes for the
canonical vocabularies. Keep one scenario-scoped `BackendScenario` owner so splitting bindings does not construct
multiple composition roots or duplicate teardown.

Binding classes are language modules, not one class per feature. Remove migrated aliases instead of retaining old
and new phrasings indefinitely. Shared parsing and table conversion may be extracted when it removes real
duplication, but step methods should continue to delegate behavior to the test-owned scenario facade rather than
reimplementing protocol or domain rules.

## Implementation plan

### Language architecture

Use these language modules. The names describe responsibilities rather than requiring these exact C# type names:

| Module | User perspective and vocabulary |
| --- | --- |
| Project configuration | A configuration author configures roles, worktrees, models, permissions, and receive modes in `blaxquad/squad.json`. Role collections and records use tables. |
| Headquarters lifecycle | An operator launches, waits for, shuts down, or otherwise terminates Headquarters through `squad-hq`; process results and diagnostics remain explicit. |
| Dashboard operations | A user sends prompts, aborts work, and answers interactions for a role; the dashboard shows role state, usage, interactions, and transcript content. |
| Agent sessions | A provider implementer observes session start and disposal, receives role prompts or responses, and emits typed replies, readiness, usage, interaction, failure, and tool events. The fake provider and its control transport stay behind these steps. |
| Transcript protocol | A UI-protocol client receives typed updates, synchronization, pages, archived entries, sequence positions, and truncation state. Ordered collections use tables. |
| Role commands | A role agent runs the documented `squad` context, handoff, `ready-for-next`, and `done-with-current` commands from its worktree. |
| Handoff delivery | An operator or role agent observes durable handoff fan-out, notification, retry, recovery, and queue state using glossary terms. |
| Public adapters | An operator selects the documented UI or provider adapter, while UI-protocol and provider-SPI users exercise their respective public contracts. |

Reqnroll must resolve one scenario-scoped owner of the `BackendScenario` facade for all language modules and one
teardown owner for every process that owner creates. Named concurrent projects and replacement launches remain
explicit children of that owner; binding classes must not construct independent default facades. Keep the facade and
all fake-provider plumbing in test support rather than exposing them in Gherkin or adding production test APIs.

Tables and parameter conversions must be strict: validate required and supported columns, parse booleans and enums
as typed values, preserve row order where it is observable, and report malformed data clearly. Do not replace the
current phrases with a generic event or action table that hides distinct behavior. Concurrent scenarios must compose
separate start, pending, release, and observe steps while retaining the existing acknowledgements and bounded waits.

### Migration slices

Slices are executed in this order, with exactly one in progress. A feature listed in two slices is deliberately split
by the named scenarios so that each handoff still has one acceptance claim.

| # | Slice | Scope | Slice acceptance |
| --- | --- | --- | --- |
| 1 | Canonical configuration and healthy lifecycle [done] | Add the complete language rules and examples to `docs/manual/test-strategy.md`; migrate `HeadquartersLifecycle.feature`; establish the shared scenario owner and lifecycle/configuration binding modules; remove `BackendScenarioLifecycle.feature` and its bindings because they specify only test-support disposal. | A configuration author can describe a multi-role project with a table, and an operator can launch a healthy Headquarters process, wait for its agents, shut it down, observe resource release and durable-worktree preservation, and relaunch it without any test-owned actor in the feature. |
| 2 | Role-directed prompts, replies, and readiness [done] | Migrate `PromptIsolationAndReadiness.feature` onto the shared modules. Fold the public request/reply path from `ProcessSpecificationDriver.feature` into this coverage, then remove that support-centric feature. Remove `FakeProviderControlProtocol.feature` and its bindings because the private transport has no user-facing contract. | Prompts reach only the addressed agent, same-role prompts serialize, different roles proceed independently, replies appear in the transcript, and `squad-hq wait-for-agent` follows session readiness using one vocabulary. |
| 3 | Abort ordering [done] | Migrate the six abort scenarios in `AbortSequencingAndTerminalRoleFailure.feature` and separate them from its terminal-failure scenario. Replace compound race phrases with reusable start, pending, release, receive, and non-receive operations. | An abort is role-scoped, cancels the active turn, cannot be overtaken by a following prompt, suppresses canceled-turn output, and remains retryable. |
| 4 | Terminal session finality [done] | Migrate the terminal-failure scenario separated in slice 3 together with `TerminatedSessionEventSuppression.feature`. | A completed or failed session remains terminal, ignores late events and interactions, rejects later commands for its role, leaves sibling roles usable, and cannot obstruct shutdown. |
| 5 | Structured interaction publication and ownership [done] | Migrate the first four scenarios of `PublishedInteractionsAndResponseOwnership.feature`. Use typed values and tables for choices, form values, URLs, optional fields, and booleans while keeping permission, input, and elicitation semantics distinct. | Every supported interaction field is published, a response reaches only its owning role and request, wrong or duplicate responses are rejected, and equal request IDs remain isolated by role. |
| 6 | Interaction cancellation and retained context [done] | Migrate the final three scenarios of `PublishedInteractionsAndResponseOwnership.feature` together with `TranscriptPendingInteractionRetention.feature`. | Abort, session failure, and shutdown cancel pending interactions, late responses are rejected, and a still-pending interaction retains visible transcript context across live-history eviction. |
| 7 | Usage and readiness publication [done] | Migrate `ActiveUsageRefresh.feature` into the dashboard and agent-session language modules. | Usage updates are visible while an agent is working, idle preserves the newest values, and a stale checkpoint cannot overwrite newer usage. |
| 8 | Transcript entry streams [done] | Migrate `TranscriptProtocolShape.feature` and `TranscriptStreamFinalization.feature`; introduce table-backed event emission and transcript observation only where the table has a fixed, typed schema. | All public entry sources retain their protocol fields, assistant and reasoning deltas update one entry, final messages replace drafts, and idle closes a reasoning stream. |
| 9 | Transcript synchronization races [done] | Migrate `TranscriptSynchronizationOrder.feature` using reusable independently-started synchronization and ordered event operations. | A synchronization racing a stream or concurrent publication reconciles every entry exactly once and in every order guaranteed by the protocol. |
| 10 | Transcript history paging and cleanup [done] | Migrate `TranscriptHistoryPaging.feature`. Keep request coordinates and archive paths behind bindings. | A UI-protocol client can combine bounded synchronization with previous pages without gaps or duplicates, retrieve retained archived entries, and observe temporary history disappear at shutdown. |
| 11 | Transcript retention and truncation [done] | Migrate `TranscriptActiveStreamRetention.feature` and `TranscriptOversizedContent.feature`. Replace repeated size-specific prose with typed counts or tables without hiding live, announcement, per-entry archive, and total-archive limits. | Active streams remain appendable across eviction, every independently bounded representation reports truncation accurately, and rotated content is reported unavailable. |
| 12 | Tool lifecycle, output, and correlation [done] | Migrate `TranscriptToolLifecycleState.feature`, `TranscriptToolOutputAggregation.feature`, and `TranscriptToolCallCorrelation.feature`. | Active-tool state follows lifecycle, cumulative output replaces one entry, progress stays separate, fallback output is used only when needed, and concurrent calls remain correlated by ID. |
| 13 | Specialized transcript presentation [done] | Migrate `TranscriptToolCommandPresentation.feature`, `TranscriptFileReadSummaries.feature`, `TranscriptSkillActivityPresentation.feature`, and `TranscriptSubagentPresentation.feature`. | Shell commands, unknown arguments, file reads, skills, and subagents retain their distinct public presentation and omission rules through reusable typed tool/event steps. |
| 14 | Stdio UI transport and selection [done] | Migrate `StdioUiProtocol.feature`, `StdioUiProtocolMultiRole.feature`, and `UiSelection.feature` into the public UI-protocol vocabulary. | `--ui stdio` honors readiness, paging, synchronization, diagnostic separation, shutdown, and concurrent line framing, while invalid UI selection reports the documented diagnostics. |
| 15 | UI command validation [done] | Migrate `UiProtocolValidation.feature`; use an examples table for invalid envelope shapes and keep raw JSON only where the wire shape itself is the contract. | Invalid envelopes and unknown roles produce their exact public protocol errors before provider dispatch, and a later valid command still succeeds. |
| 16 | Shutdown admission and dispatch draining [done] | Migrate `ShutdownCommandAdmission.feature` and `StdioTransportConcurrentDispatchAndShutdown.feature` with composable begin-shutdown, pending-operation, release, and exit steps. | Shutdown drains commands admitted before closure, rejects later commands without side effects, and waits for in-flight stdio dispatch before disposing the protocol session. |
| 17 | Host ownership and project isolation [done] | Migrate `HostOwnership.feature` and `HostCoexistence.feature`; share the same `squad-hq` command language for default, equivalent-path, linked-worktree, and named-project contexts. | One project has one host, readiness and shutdown discover the right host, stale ownership recovers, and stopping one project never affects another. |
| 18 | Startup interruption and external termination | Migrate `HeadquartersEarlyShutdown.feature` and `HeadquartersTermination.feature`; express readiness watches, independently initiated shutdown, input closure, and platform cancellation as separate operations. | Headquarters terminates cleanly before or during startup and after readiness, never admits late work, disposes started sessions, preserves durable files, and permits replacement launch. |
| 19 | Provider failure and cleanup | Migrate `HeadquartersPartialStartupFailure.feature`, `HeadquartersTerminalProviderFailure.feature`, and `HeadquartersCleanupDiagnostics.feature`. | Startup, per-session, backend-wide, and cleanup failures preserve their distinct outcomes and diagnostics, dispose only the appropriate sessions, retain durable files, release ownership, and permit replacement launch. |
| 20 | Provider loading and packaging | Migrate `AgentProviderSelection.feature` and `ProviderPackaging.feature` to operator-facing `squad-hq` and public provider-adapter terms. | Explicit provider selection fails clearly for every invalid descriptor, the executable has no compile-time Copilot dependency, and publish variants contain exactly their documented provider assets. |
| 21 | Role context | Migrate `Context.feature` with canonical project configuration and explicit `squad context` actions. | A role agent resolves its role from each worktree and JSON context identifies the project, role worktree, and shared source without a legacy environment variable. |
| 22 | Handoff authoring | Migrate `Handoffs.feature`; represent recipient collections structurally rather than as comma-separated values. | `squad handoff` creates valid Git and note handoffs, reports all repairable draft errors, preserves invalid drafts, and exposes durable queue results in handoff vocabulary. |
| 23 | Task and batch receive modes | Migrate `TaskQueue.feature`, `BatchQueue.feature`, and the role-command scenarios from `Recovery.feature`. Reuse command/result steps while retaining the distinct task and batch state machines. | `ready-for-next` and `done-with-current` select, resume, complete, and reject task or batch state exactly according to the configured receive mode, priority, and worktree identity. |
| 24 | Handoff delivery and recovery | Migrate `Delivery.feature`, the delivery/restart scenarios from `Recovery.feature`, and `HeadquartersHandoffPumpFailure.feature`. Remove the final migrated aliases and delete `BackendScenarioSteps` if empty. | Delivery persists before notification, retries and restarts neither lose nor duplicate work, unavailable or busy recipients recover correctly, and a real delivery-pump failure reports its own diagnostic while preserving queued work. |

### Definition of done for every slice

- Rewrite the complete slice scope from its identified user perspective and keep every existing behavioral assertion.
- Reuse or extend the shared language modules; do not introduce feature-specific dialects or another scenario facade.
- Remove every alias and binding made unused by the slice rather than retaining compatibility wording.
- Preserve deterministic synchronization through observable acknowledgements and bounded waits; do not add sleeps.
- Run the focused scenarios for the slice and then the complete `squad.Specs` suite.
- Do not change production behavior or add production APIs for test convenience.
- Do not edit generated `*.feature.cs` files by hand.

## Slice 18 review (1d0186e9e8) — changes requested

Finding 1 on 7b0c18e7ea is addressed: both early-shutdown races compose independently initiated shutdown, a separate user send, and an observe-completion step.

Finding 2's launch-phrase unification is incomplete: the Echo vs fake split moved into a Gherkin Given that names the fake-provider control transport.

### Finding 1 — High

- **Location:** `src/squad.Specs/Features/HeadquartersEarlyShutdown.feature` (`the fake-provider control transport is enabled`); `src/squad.Specs/StepDefinitions/HeadquartersLifecycleSteps.cs`.
- **Violated behavior:** Migrated features must not mention a fake provider or a control pipe as a Gherkin actor. The agent-session module keeps the fake provider and its control transport behind those steps. Fixture selection stays behind bindings. The issue lists `the backend scenario has enabled the fake-provider control transport` as the example of test infrastructure becoming the actor.
- **Root cause:** Unifying the no-handshake launch required opt-in session observation without hanging stdin-close-before-ready, and that opt-in was written as the old fixture Given without `backend scenario`.
- **Required outcome:** Keep one no-handshake Headquarters launch phrase. Enable session-lifecycle observation behind bindings or as Headquarters-facing language that does not mention the fake provider or a control pipe. Do not put `fake-provider`, `control transport`, or `control pipe` in Gherkin.

## Slice 18 review (7b0c18e7ea) — changes requested

Readiness watches, input closure, platform cancellation, launch/shutdown, and replacement reuse Headquarters lifecycle language. Two shutdown steps still hard-code a concurrent send.

### Finding 1 — High

- **Location:** `src/squad.Specs/Features/HeadquartersEarlyShutdown.feature` (`the operator requests shutdown as soon as it is reachable while sending ...`, `the operator requests shutdown while sending ...`); `src/squad.Specs/StepDefinitions/HeadquartersLifecycleSteps.cs`.
- **Violated behavior:** Slice 18 must express independently initiated shutdown and other operations as separate steps. Race language must compose start/pending/observe steps rather than one sentence that hard-codes a specific pair of concurrent actions. Exact race orchestration may stay atomic in the driver, but Gherkin must express the reusable relationship.
- **Root cause:** Both early-shutdown scenarios still fire host-control shutdown and a late prompt in one When, the same compound pair the old backend-scenario phrases used.
- **Required outcome:** In `HeadquartersEarlyShutdown.feature`, initiate shutdown independently of the late prompt, then send the prompt as a separate user/dashboard operation. Remove the compound bindings if nothing else uses them. Keep deterministic race orchestration (do not await shutdown completion before the send).

### Finding 2 — Medium

- **Location:** `src/squad.Specs/Features/HeadquartersTermination.feature` (`the operator launches a squad host without completing the ready handshake`); `src/squad.Specs/StepDefinitions/HeadquartersLifecycleSteps.cs` (that binding vs `the operator launches Headquarters without completing the ready handshake`).
- **Violated behavior:** No two bindings may differ only by provider choice. Fixture selection stays behind bindings. Slice 17 also forbade a second "squad host" launch dialect beside Headquarters.
- **Root cause:** The two no-handshake launch steps differ only by Echo vs fake-provider factory, and the Echo variant was named `squad host` to tell them apart.
- **Required outcome:** Use one no-handshake Headquarters launch phrase. Keep Echo vs fake-provider selection in the binding or in test setup, not as a second Gherkin actor/noun.

## Slice 17 review (94f1f13a72) — accepted

**Status: complete (94f1f13a72).** Finding on bf4e3dc2c3 was addressed: `HostOwnershipSteps` uses the scenario-scoped `BackendScenario`, and live-host shutdown/wait-for-agent reuse Headquarters lifecycle vocabulary. Equivalent-path, linked-worktree, empty-project, and named-project stay as context on those verbs.

### Finding 1 — High

- **Location:** `src/squad.Specs/Features/HostOwnership.feature` (shutdown and `wait-for-agent` steps); `src/squad.Specs/StepDefinitions/HostOwnershipSteps.cs`.
- **Violated behavior:** Slice 17 must share the same `squad-hq` command language for default, equivalent-path, linked-worktree, and named-project contexts. Definition of done requires reusing shared language modules and forbids a feature-specific dialect. Headquarters lifecycle already has `the operator shuts down Headquarters`, `Headquarters exits with code 0`, and `the operator begins waiting for role ... to become ready with squad-hq wait-for-agent`. `HostCoexistence.feature` already shuts down Headquarters and asserts exit code 0.
- **Root cause:** The migration renamed the actor but kept HostOwnership-only verbs (`requests squad shutdown`, `the operator's shutdown succeeds`, `the host process exits`, `begins waiting for the ... agent`) and a privately constructed `BackendScenario`, so those scenarios cannot reuse the Headquarters lifecycle steps.
- **Required outcome:** In both migrated features, launch, shutdown, and wait-for-agent use the shared Headquarters/`squad-hq` vocabulary. Equivalent-path, linked-worktree, empty-project, and named-project stay as context on those same verbs. Do not keep a second "squad host" / "requests squad shutdown" dialect in `HostOwnership.feature`.

## Slice 16 review (5d11e09ec0) — accepted

**Status: complete (5d11e09ec0).** Finding on 56b3f5623e was addressed: the unused `the backend scenario observes a protocol error mentioning` binding is gone. Canonical user-observation wording remains; session-disposal hold/complete aliases stay for `HeadquartersCleanupDiagnostics.feature`.

### Finding 1 — Medium

- **Location:** `src/squad.Specs/StepDefinitions/BackendScenarioSteps.cs` (`Then("the backend scenario observes a protocol error mentioning {string}")`).
- **Violated behavior:** Definition of done requires removing every alias this slice made unused. Slice 16 migrated the last callers of this phrase to `the user observes a protocol error mentioning`.
- **Root cause:** After `ShutdownCommandAdmission.feature` switched to the canonical user-observation step, the BackendScenarioSteps phrase was left even though no remaining feature uses it.
- **Required outcome:** Delete the unused `the backend scenario observes a protocol error mentioning` binding. Keep `the user observes a protocol error mentioning`. Keep `the backend scenario arms/completes... session disposal` until `HeadquartersCleanupDiagnostics.feature` migrates.

## Slice 15 review (67fbce653f) — accepted

**Status: complete (67fbce653f).** `UiProtocolValidation.feature` uses canonical launch and a UI-protocol-client actor. Invalid envelope shapes and exact public errors live in the examples table; the C# `InvalidUiMessage` switch is gone. Unknown-role rejection and a later successful command remain.

## Slice 14 review (184ab3de9a) — accepted

**Status: complete (184ab3de9a).** Stdio protocol and UI-selection features use operator launch/shutdown, UI-protocol-client wire commands, and canonical configuration. `StdioUiProtocolSteps` shares the scenario-scoped `BackendScenario`; unused stdio-specific launch/shutdown bindings were removed.

## Slice 13 review (e422133608) — accepted

**Status: complete (e422133608).** Finding on 6e965716b6 was addressed: the unused `the backend scenario requests a fresh transcript synchronization` binding is gone. Canonical user-request wording remains; `the backend scenario does not observe the transcript...` stays for `ShutdownCommandAdmission.feature`.

### Finding 1 — Medium

- **Location:** `src/squad.Specs/StepDefinitions/BackendScenarioSteps.cs` (`When("the backend scenario requests a fresh transcript synchronization")`).
- **Violated behavior:** Definition of done requires removing every alias this slice made unused. Slice 13 migrated the last callers of this phrase to `the user requests a fresh transcript synchronization for role`.
- **Root cause:** After the four presentation features switched to the canonical user-request step, the BackendScenarioSteps phrase was left even though no remaining feature uses it.
- **Required outcome:** Delete the unused `the backend scenario requests a fresh transcript synchronization` binding. Keep `the user requests a fresh transcript synchronization for role` as the canonical wording. Keep `the backend scenario does not observe the transcript...` until `ShutdownCommandAdmission.feature` migrates.

## Slice 12 review (13e2b4de60) — accepted

**Status: complete (13e2b4de60).** The three tool features use canonical launch/shutdown, dashboard active-tool and transcript-update observations, and user-requested synchronization. Unused `most recently observed` index bindings were removed; `backend scenario` transcript-update and active-tool aliases remain for slice 13.

## Slice 11 review (e97960ecb7) — accepted

**Status: complete (e97960ecb7).** Finding on e4292c53ed was addressed: both active-stream scenarios emit the live-retention crossing burst as `5 system messages with 250000 characters each`. Assistant and reasoning streams stay distinct.

### Finding 1 — Medium

- **Location:** `src/squad.Specs/Features/TranscriptActiveStreamRetention.feature` (both scenarios: five consecutive `the "coder" agent emits a system message with 250000 characters` steps).
- **Violated behavior:** Slice 11 must replace repeated size-specific prose with typed counts or tables without hiding live, announcement, per-entry archive, and total-archive limits. The suite already has `the {string} agent emits {int} system messages with {int} characters each`.
- **Root cause:** The migration rewrote actors but left the live-retention crossing burst as five identical one-message steps instead of one typed count of 5 messages at the 250000-character live bound.
- **Required outcome:** In both active-stream scenarios, emit the crossing burst as one typed count (5 messages of 250000 characters each). Keep the 250000 live-retention bound visible; do not fold assistant and reasoning into one step that hides those distinct streams.

## Slice 10 review (1f66184f55) — accepted

**Status: complete (1f66184f55).** `TranscriptHistoryPaging.feature` uses canonical launch/shutdown, dashboard synchronization, and UI-protocol-client paging/archive requests. Page coordinates stay behind bindings; old `backend scenario` page/archive aliases remain for slice 11.

## Slice 9 review (1d5cf1ab21) — accepted

**Status: complete (1d5cf1ab21).** Finding on 61ec7d61fb was addressed: the concurrent burst now begins an independently started synchronization, then emits the system messages as a table. The compound race binding was removed.

### Finding 1 — High

- **Location:** `src/squad.Specs/Features/TranscriptSynchronizationOrder.feature` (`When the "coder" agent concurrently emits these system messages while a transcript synchronization races them`); `src/squad.Specs/StepDefinitions/BackendScenarioSteps.cs` (`WhenTheAgentConcurrentlyEmitsTheseSystemMessagesWhileATranscriptSynchronizationRacesThem`).
- **Violated behavior:** Slice 9 must migrate this feature using reusable independently-started synchronization and ordered event operations. Race language must compose start/pending/observe steps rather than one sentence that hard-codes a specific pair of concurrent actions. Exact race orchestration may stay atomic in the driver, but Gherkin must express the reusable relationship.
- **Root cause:** The compound When is exclusive to this feature (other files only share the reconciled-transcript Then). It still fires `RequestTranscriptSynchronization` and the burst emits in one binding instead of an independently started synchronization plus a table of events.
- **Required outcome:** In `TranscriptSynchronizationOrder.feature`, start synchronization independently of the burst, then emit the system messages as an ordered/table operation. Remove the compound binding if nothing else uses it. Keep deterministic race orchestration (do not await the sync acknowledgement before the emits).

## Slice 8 review (4dc5240d95) — accepted

**Status: complete (4dc5240d95).** Finding on 186dad1e69 was addressed: both migrated features observe transcript updates and request synchronization in dashboard/user language, with no `backend scenario` actor. Old BackendScenarioSteps phrases remain as aliases for unmigrated transcript features.

### Finding 1 — High

- **Location:** `src/squad.Specs/Features/TranscriptProtocolShape.feature` and `src/squad.Specs/Features/TranscriptStreamFinalization.feature` (`the backend scenario observes a transcript update...`, `the backend scenario requests a fresh transcript synchronization`).
- **Violated behavior:** Slice 8 must rewrite both features from their identified user perspective. Definition of done forbids a test-owned actor in the migrated feature. Transcript updates and synchronization are dashboard or UI-protocol-client observations; `BackendScenario` must not appear as a Gherkin actor. Slice 2 already required canonical wording in the migrated file and old phrases only as aliases for unmigrated features.
- **Root cause:** Setup and shutdown were rewritten, but incremental transcript observation and sync requests kept the `backend scenario` aliases inside this slice's features instead of dashboard/UI-protocol wording. A canonical `the user requests a fresh transcript synchronization for role` step already exists from earlier slices.
- **Required outcome:** In both migrated features, do not name `backend scenario`. Observe transcript updates and request synchronization in dashboard or UI-protocol-client language. Keep the old BackendScenarioSteps phrases only as aliases for files this slice does not migrate.

## Slice 7 review (fb71cf69f0) — accepted

**Status: complete (fb71cf69f0).** `ActiveUsageDashboardRefresh.feature` covers mid-turn usage, idle preserving the latest values, and a stale AIC checkpoint not overwriting newer usage, using dashboard and agent-session language. The feature-specific `ActiveUsageRefreshSteps` owner was removed.

## Slice 6 review (60f5a594a2) — accepted

**Status: complete (60f5a594a2).** `InteractionCancellationAndTranscriptRetention.feature` covers abort, session-failure, shutdown cancellation, and live-retention of pending-permission transcript context in dashboard, agent, and Headquarters-lifecycle language. Unused abort/permission/input aliases and `ParseChoices` were removed; aliases still required by later slices remain.

## Slice 5 review (0fe774f64c) — accepted

**Status: complete (0fe774f64c).** Findings on 109fc0af0a were addressed: unused pending-elicitation observation aliases are gone, and migrated input choices are a `choice` table with freeform as a separate boolean. CSV aliases remain only for unmigrated features.

### Finding 1 — Medium

- **Location:** `src/squad.Specs/StepDefinitions/BackendScenarioSteps.cs` (`Then("the backend scenario observes a pending elicitation {string} for role {string} with prompt {string} and mode {string}")` and the sibling binding that adds `and url {string}`).
- **Violated behavior:** Definition of done requires removing every alias this slice made unused. Slice 5 also forbids two bindings that differ only by an optional field; URL presence belongs in one table, which the migrated feature already uses.
- **Root cause:** Those two observation phrases were exclusive to the migrated publication scenario. After the dashboard table step replaced them, they were left in `BackendScenarioSteps` even though no remaining feature uses them.
- **Required outcome:** Delete both unused pending-elicitation observation bindings. Keep only aliases still required by `PendingInteractionCancellation.feature` or `TranscriptPendingInteractionRetention.feature`.

### Finding 2 — Medium

- **Location:** `src/squad.Specs/Features/InteractionPublicationAndOwnership.feature` (input request/observation tables with a `choices` column); `src/squad.Specs/StepDefinitions/BackendScenarioSteps.cs` and `DashboardOperationsSteps.cs` (`ParseChoices` on that column).
- **Violated behavior:** Slice 5 must use typed values and tables for choices, and the language rules forbid comma-separated lists when a table can express the collection. Moving `main,develop` from step text into a single table cell does not replace that encoding.
- **Root cause:** The new input record table treats `choices` as a comma-separated cell (now with trimmed spaces) instead of a collection of rows, so the canonical vocabulary still parses a CSV list.
- **Required outcome:** In the migrated feature, express choices as a table of values (one choice per row; omit or use an empty table when there are none) and keep freeform as a typed boolean. Retain the old CSV observation/request aliases only while unmigrated features still use them.

## Slice 4 review (ff2a535116) — accepted

**Status: complete (ff2a535116).** `TerminalSessionFinality.feature` covers the four terminal-session scenarios in dashboard, agent, and Headquarters-lifecycle language without a test-owned actor. Unused latest-status and compound-synchronization bindings were removed; aliases still required by later slices remain.

## Slice 3 review (adc4456f3d) — accepted

**Status: complete (adc4456f3d).** Finding on 04cb1511c4 was addressed: `the backend scenario requests an abort for role` is restored as an alias for unmigrated features; `AbortOrdering.feature` keeps `the user aborts role`.

### Finding 1 — High

- **Location:** `src/squad.Specs/StepDefinitions/BackendScenarioSteps.cs` (removed `When("the backend scenario requests an abort for role {string}")`); `src/squad.Specs/Features/PublishedInteractionsAndResponseOwnership.feature` (still uses that step).
- **Violated behavior:** Slice 3 migrates only the six abort scenarios. Definition of done requires removing aliases that the slice made unused, not bindings still required by unmigrated features. Every existing behavioral assertion outside this slice must keep working.
- **Root cause:** The abort-request binding was deleted when `AbortOrdering.feature` switched to `the user aborts role`, but `PublishedInteractionsAndResponseOwnership.feature` (slices 5–6) still says `the backend scenario requests an abort for role "coder"`. The hold/release/fail abort phrases were unused elsewhere and could be renamed; the abort-request phrase was not.
- **Required outcome:** Restore `the backend scenario requests an abort for role {string}` as an alias of the same `RequestAbort` operation until no remaining feature uses it. Keep `the user aborts role` as the canonical wording in `AbortOrdering.feature`.

## Slice 2 review (d51034a07e) — accepted

**Status: complete (d51034a07e).** Finding on 95c2f67f47 was addressed: `PromptIsolationAndReadiness.feature` uses dashboard-user prompt and transcript wording, does not name `backend scenario` or the fake-agent control pipe, and keeps the old BackendScenarioSteps phrases as aliases for unmigrated features.

### Finding 1 — High

- **Location:** `src/squad.Specs/Features/PromptIsolationAndReadiness.feature` (prompt-send and transcript-observation steps; feature description); `src/squad.Specs/StepDefinitions/BackendScenarioSteps.cs` (`WhenTheBackendScenarioSendsThePromptToRole`, `ThenTheBackendScenarioObservesTheTranscriptForRoleContaining`).
- **Violated behavior:** Slice 2 must rewrite `PromptIsolationAndReadiness.feature` from its identified user perspective onto the shared modules. Definition of done for every slice requires that rewrite and forbids a test-owned actor in the migrated feature. Dashboard operations own sending a prompt; transcript observation is a user-visible outcome. `BackendScenario` and the fake-agent control pipe must not appear as a Gherkin actor, observation channel, or specified result. Slice 1 already showed the required pattern: introduce canonical wording in the migrated feature and keep old aliases only while unmigrated features still use them.
- **Root cause:** The migration reused `the backend scenario sends the prompt ...` and `the backend scenario observes the transcript ...` so it would not add parallel phrases, instead of adding dashboard/transcript vocabulary and using that in this feature. The feature description still names the fake-agent control pipe as an observation channel.
- **Required outcome:** In `PromptIsolationAndReadiness.feature`, an identifiable user sends prompts and a user-visible transcript assertion observes replies — matching the canonical examples in `docs/manual/test-strategy.md`. Do not name `backend scenario` or the fake-agent control pipe in that feature. Keep the old BackendScenarioSteps phrases only as aliases for files this slice does not migrate.

## Slice 1 review (1018ba7c76) — accepted

**Status: complete (1018ba7c76).** Findings on 5cc7996c17 were addressed: launch/relaunch Gherkin no longer names test providers, `test-strategy.md` states the language-architecture and strict-table rules, and the configuration table validates required/supported columns.

### Finding 1 — High

- **Location:** `docs/manual/test-strategy.md` (Gherkin language examples and Agent sessions / Headquarters lifecycle wording); `src/squad.Specs/Features/HeadquartersLifecycle.feature` launch and relaunch steps; `src/squad.Specs/StepDefinitions/HeadquartersLifecycleSteps.cs` (`WhenTheOperatorLaunchesHeadquartersWithTheFakeProvider`, `WhenTheOperatorLaunchesANewHeadquartersAgainstTheSameProjectWithTheEchoProvider`).
- **Violated behavior:** Slice 1 must add the complete language rules and migrate the healthy-lifecycle feature without exposing test-owned plumbing in Gherkin. The language architecture requires keeping the facade and all fake-provider plumbing in test support rather than Gherkin, keeping fixture selection in test setup when it is not the specified behavior, and teaching launch as `squad-hq launch`. An operator must be able to launch, wait, shut down, and relaunch without a test-owned actor or fixture in the feature.
- **Root cause:** The canonical examples and the migrated spine kept provider-fixture selection (`fake provider`, `echo provider`) as visible Gherkin instead of hiding it in the launch bindings. The test-strategy examples therefore contradict both the issue's candidate form and the Agent sessions rule that the fake provider stays behind those steps. The Gherkin language section also omits the language-architecture constraints on one scenario-scoped facade owner, replacement launches as children of that owner, no independent default facades, and keeping fake-provider plumbing out of Gherkin.
- **Required outcome:** `docs/manual/test-strategy.md` carries the complete language-architecture rules, and its examples do not name the fake or echo providers. `HeadquartersLifecycle.feature` launch and relaunch steps name only operator/`squad-hq` behavior; fake and echo factory selection stays in bindings or test setup.

### Finding 2 — Medium

- **Location:** `src/squad.Specs/StepDefinitions/ProjectConfigurationSteps.cs` (`GivenBlaxquadSquadJsonConfigures`); `docs/manual/test-strategy.md` table conventions (missing strict-validation rule).
- **Violated behavior:** Tables and parameter conversions must be strict: validate required and supported columns, parse booleans and enums as typed values, preserve row order where it is observable, and report malformed data clearly. Slice 1 must add that complete rule to the test strategy and apply it to the new configuration table.
- **Root cause:** The new step reads only `row["role"]`, silently ignores every other column, and has no header or malformed-data check, so a table that includes `model` or `receive mode` appears to configure those fields while `ConfigureRoles` still writes default agent records.
- **Required outcome:** The project-configuration table step declares the columns it actually applies (at least `role` for this slice), rejects missing or unknown columns with a clear error, preserves row order, and writes every accepted column into `blaxquad/squad.json`. The same strict-table rule is stated in `docs/manual/test-strategy.md`.

## Acceptance criteria

- `docs/manual/test-strategy.md` requires a user perspective and defines the canonical actors, public nouns, verbs,
  parameter conventions, and table conventions.
- Every feature is written for an identifiable operator, role agent, configuration author, UI-protocol client,
  provider implementer, or other user of a documented public surface.
- Feature language comes from the glossary, manual, CLI help, public configuration, visible UI, or a supported
  protocol/SPI. Public technical terms such as `squad`, `squad-hq`, `blaxquad/squad.json`, roles, agents, models,
  Headquarters, transcripts, handoffs, and protocol messages remain available where relevant.
- No feature names `BackendScenario`, a role-interaction scenario, a backend-spec fixture, a fake-provider fixture or
  control pipe, a recording object, or another test-support type as an actor or observable result.
- Features currently devoted to test infrastructure are either recast to specify public behavior or removed when
  they provide no independent user-facing contract.
- Project/role setup, headquarters lifecycle, prompt delivery, agent reply, transcript content, and shutdown each
  have one canonical vocabulary used across feature files.
- No two bindings differ only by singular versus plural wording, provider choice, a boolean, a count, content
  length, or the presence of an optional field. Such variation uses typed parameters, tables, scenario outlines, or
  explicit setup state as appropriate.
- Lists and structured values are not passed as comma-separated or string-encoded values when a Reqnroll table or
  typed conversion expresses them directly.
- Ordered provider events and groups of transcript or interaction observations use reusable table-backed steps
  where that improves composition; distinct observable semantics remain explicit.
- Race and lifecycle scenarios compose reusable pending/start/release/observe operations instead of adding
  scenario-specific multi-action sentences.
- Every remaining binding used by only one scenario has been reviewed and represents a genuinely unique observable
  contract, not data variation or support-class naming.
- `BackendScenarioSteps` no longer owns unrelated lifecycle, interaction, transcript, provider-event, workspace, and
  protocol vocabularies. The split bindings share one scenario-scoped facade and one teardown owner.
- Migrated aliases and unused step definitions are removed. The refactoring leaves no compatibility vocabulary that
  merely preserves the old wording.
- All existing black-box behaviors and deterministic synchronization guarantees remain covered, and the complete
  `squad.Specs` suite passes without production-code changes made solely for the tests.

## Non-goals

- Reorganizing `src/squad.Specs/Support`, deciding which support exceptions are necessary, or classifying fake
  implementations. That work is tracked separately by `step support is huge.md`.
- Replacing meaningful domain language with generic `execute action` or `observe value` steps.
- Hiding fields, messages, commands, or failure details that are part of a documented public protocol or API.
- Changing product behavior or adding lower-level tests.