# Backend test strategy

## Purpose

The C# backend is tested as a black box through the same process and protocol boundaries used by users and agents.
Tests focus on supported use cases rather than the internal structure of `SquadApplication`, `SquadViewModel`, runtime
coordination, or protocol implementation helpers.

All backend tests remain in `src/squad.Specs`. Do not create test assemblies aligned with product assemblies. Mirroring
the production module graph in the test project graph would couple tests to the design they are meant to exercise from
the outside.

The target test system runs:

- the real published `squad.exe`;
- the real published `squad-hq.exe`;
- the real C# UI JSON protocol without Photino or Vue;
- a fake provider implementing the provider-neutral agent SPI; and
- real Git, filesystem, workspace, host-control, and handoff behavior.

The main backend suite must not reference or load `squad.AgentProvider.CopilotSdk`.

## Test boundary

```text
squad.Specs
  |
  |-- runs squad.exe
  |     `-- real Git, filesystem, worktrees, and handoff mailbox
  |
  |-- runs squad-hq.exe --ui stdio --provider <fake-provider>
  |     |-- real workspace preparation
  |     |-- real host lease and control endpoint
  |     |-- real handoff delivery
  |     |-- real application and host runtime
  |     |-- UiProtocolSession over stdin/stdout
  |     `-- fake provider loaded from squad.AgentProvider.Fake.dll
  |
  |-- headless UI client <-------- JSON over stdin/stdout
  `-- fake-agent controller <----- test-owned named pipe
```

The process and wire protocols are the public test boundary. Product object graphs are not.

## Principles

### Test use cases, not implementation structure

Gherkin scenarios describe an operator, user, or agent action and its observable result. They do not mention:

- `SquadApplication` or `SquadViewModel`;
- session registries or lifecycle coordinator types;
- concrete provider sessions;
- callback counts or injected delegates;
- internal collections or mutable state;
- protocol journals, publishers, or serializers; or
- exact helper-call order unless it is itself an externally observable contract.

Examples of supported scenario language are:

- a user sends a prompt and the selected role receives it;
- an agent reply appears in the role transcript;
- a permission request is shown and the user's answer reaches the requesting agent;
- a handoff is delivered once and wakes the recipient;
- a failed provider session makes the role unavailable;
- headquarters shuts down cleanly after a control request; and
- queued work survives headquarters restart.

### Keep one backend specification project

All feature files, step definitions, the scenario driver, and test support remain in `squad.Specs`. The fake
agent-provider adapter is the one exception: it lives in its own `squad.AgentProvider.Fake` module, loaded into the
launched `squad-hq` process through the same explicit `--provider` descriptor a real provider uses, so it is
exercised across the identical assembly-loading boundary a production provider crosses. Only its reflection-loaded
fixture factory types (`FakeAgentProviderFactory`, `ValidProviderFixtureFactory`, `ThrowingProviderFixtureFactory`,
`IncompatibleProviderFixture`) are public; its runtime, sessions, event stream, and control transport stay internal,
with `squad.Specs` granted access through `InternalsVisibleTo`.

Internal folders and helper types may separate responsibilities inside `squad.Specs`, but those are test
implementation details. They must not become new assemblies or reproduce the product module boundaries.

`src/squad.Specs/Support` is organized into four responsibility folders, one owner per folder, with namespaces matching
each folder. This is an ownership map for navigating test support, not a mandate to add a layer around every file:

| Folder | Namespace | Owner |
| --- | --- | --- |
| `Scenarios` | `squad.Specs.Support.Scenarios` | The scenario composition root (`BackendScenario`), its role/command adapters, and the temporary Git workspace it drives. `BackendScenario` is the single scenario-scoped owner of normal shutdown, emergency teardown, and every replacement launch or explicit independent-project child it creates. |
| `Processes` | `squad.Specs.Support.Processes` | Child-process execution, captured command results, and process diagnostics. |
| `Ui` | `squad.Specs.Support.Ui` | The headless UI protocol client and its decoded transcript/synchronization/page observations. |
| `Mailboxes` | `squad.Specs.Support.Mailboxes` | Durable handoff and task mailbox setup and observation fixtures. |

The fake agent-provider adapter's own folders live under `src/squad.AgentProvider.Fake` instead: its top level
(namespace `squad.AgentProvider.Fake`) holds the fake provider fixtures and provider-selection fixtures, and
`Control` (namespace `squad.AgentProvider.Fake.Control`) holds the private fake-provider control transport,
protocol, handlers, and observation state.

### Keep production infrastructure real

Use real:

- Git repositories and worktrees;
- filesystem-backed configuration and handoff queues;
- `squad` command execution;
- headquarters host ownership and control;
- handoff polling and delivery;
- application lifecycle and state projection; and
- UI protocol framing, commands, snapshots, and transcript messages.

Do not introduce filesystem, Git, clock, process, host-lease, or handoff test abstractions preemptively. Add a seam only
when production has a genuine alternative implementation or an external dependency cannot be exercised reliably.

### Fake only the external agent provider

Provider behavior is external, expensive, and nondeterministic. Tests replace it through the same provider-neutral SPI
that production providers implement.

The fake provider is test code. It must not introduce a fake-provider mode, callback, conditional branch, or test API
inside product assemblies.

### Drive the real UI protocol without the visual UI

Backend tests do not run Vue or Photino. They send and receive the same versioned JSON messages used by the dashboard
through `UiProtocolSession`.

Frontend rendering and browser behavior remain the responsibility of focused Playwright tests. Backend Gherkin tests
assert the authoritative C# state and protocol behavior that the frontend consumes.

## Test-owned API

Step definitions use one test-owned scenario facade. Names may evolve, but its responsibilities should remain similar
to:

```csharp
await using var scenario = await BackendScenario.StartAsync(workspace);

await scenario.Ui.SendPromptAsync("architect", "Implement X");
await scenario.Agent("architect").ExpectPromptAsync("Implement X");

await scenario.Agent("architect").ReplyAsync("Done");
await scenario.Ui.WaitForTranscriptAsync("architect", "Done");

await scenario.Cli.RunSquadAsync("architect", "handoff", draftPath);
await scenario.Ui.WaitForRoleStatusAsync("implementer", "working");
```

The facade exposes only user-oriented capabilities.

### Workspace

- Create a temporary configured project and its roles.
- Initialize and commit through real Git.
- Locate role worktrees.
- Seed prerequisite durable state when a scenario cannot create it through a command.
- Observe mailbox state through semantic test-owned values.

Raw paths and file formats remain inside the workspace support unless the format itself is the contract being tested.

### CLI

- Publish and run the real `squad.exe`.
- Publish and run the real `squad-hq.exe`.
- Capture exit code, standard output, standard error, and process lifetime.
- Run `squad` from the same worktree and environment an agent would use.
- Request shutdown and readiness through the real `squad-hq` commands.

### Headless UI

- Complete the normal UI-ready handshake.
- Send real UI protocol commands for prompts, aborts, interaction responses, transcript requests, and other supported
  operations.
- Receive snapshots, transcript synchronization, incremental transcript updates, pages, and protocol errors.
- Wait for semantic state with bounded deadlines rather than arbitrary sleeps.

The test client may maintain the minimum state needed to interpret the protocol. It must not duplicate authoritative
domain rules owned by C# or frontend behavior owned by Vue.

### Fake agent

- Wait for a role session to start.
- Observe prompts, harness messages, aborts, and interaction responses.
- Emit provider-neutral assistant, reasoning, tool, usage, interaction, and failure events; express busy and ready
  through an outstanding prompt and `AgentIdleEvent`, the same signals a real provider publishes.
- Delay or fail supported provider operations when a meaningful use case requires it.
- Complete or fail a session.

Gherkin steps use semantic operations such as `Reply`, `RequestPermission`, or `FailSession`; they do not construct raw
provider event records.

### Lifecycle

- Start headquarters and wait for readiness.
- Observe early startup failure and process diagnostics.
- Request shutdown through the real host-control command.
- Wait for clean process exit.
- Collect diagnostics and perform bounded emergency cleanup when a failed scenario cannot shut down normally.

## Required production SPIs

### Agent provider factory

The existing provider-neutral runtime contracts remain the authoritative SPI:

- `IAgentBackend`;
- `IAgentRuntime`;
- `IAgentSession`; and
- typed `AgentEvent` values.

Headquarters additionally needs one process-time provider factory. The current `IRuntimeModeFactory` should be reduced
to a cohesive contract conceptually equivalent to:

```csharp
public interface IAgentProviderFactory
{
    string Name { get; }

    Task<IAgentBackend> CreateAsync(
        AgentBackendContext context,
        CancellationToken cancellationToken);
}
```

The exact name is not important. The contract is:

- context is a prepared value, not a deferred `Func<AgentBackendContext>`;
- provider preparation and backend creation form one cohesive operation;
- the returned backend retains ownership of provider-runtime generations as today; and
- the factory contains no test-specific members.

### Explicit provider loading

`squad-hq` currently references and constructs `CopilotSdkRuntimeModeFactory` directly. To run the actual executable
without `squad.AgentProvider.CopilotSdk`, provider selection must happen at process composition.

Headquarters loads a provider factory selected explicitly by a trusted command-line option or installation manifest:

- the production package selects the Copilot SDK provider by default;
- backend specs explicitly select the fake provider from `squad.AgentProvider.Fake.dll`; and
- no provider assembly is discovered or loaded from the target workspace.

Loading arbitrary code from a repository configuration is prohibited. An alternate provider path must be an explicit
choice by the process launcher.

The provider-neutral headquarters executable must not have a compile-time dependency on `squad.AgentProvider.CopilotSdk`. Production
packaging may include the Copilot provider as the default plug-in, while backend-spec publication may omit it.

### Headless UI host

The existing `IWindowHost` is sufficient. Add a headless implementation that owns a `UiProtocolSession` and:

- reads newline-delimited JSON commands from standard input;
- writes newline-delimited JSON protocol messages to standard output;
- reserves standard error for process diagnostics;
- waits for the regular UI-ready command during startup;
- forwards session-start notification to the protocol session; and
- remains open until host-control shutdown, cancellation, or input closure.

For example:

```text
squad-hq launch --ui stdio --provider <fake-provider> <workspace>
```

This is a supported headless transport, not a test callback. Photino remains the default visual adapter.

No additional UI SPI is required unless the Photino assembly must also be physically absent from the headless
publication. In that case, provider-style loading may be introduced for an `IWindowHost` factory as a separate,
cohesive decision.

## Fake-provider control channel

The fake provider runs inside the `squad-hq` child process, while the scenario driver runs inside the test process.
They need a private control channel.

Use a uniquely named local pipe with a random per-scenario token:

```text
squad.Specs test runner <-- test-only JSON --> fake provider in squad-hq
```

The fake provider may obtain its endpoint from an environment variable understood only by test code. Headquarters does
not parse, forward, or understand the fake-provider protocol.

The channel carries:

- session-started and session-disposed observations;
- received prompts, harness messages, aborts, and interaction responses;
- commands to emit provider events;
- commands to complete or fail operations and sessions; and
- acknowledgements used for deterministic waiting.

Both ends and all DTOs live in `squad.Specs`. This is not product API.

## Process synchronization

- Use protocol messages, provider acknowledgements, host readiness, process completion, and durable-state predicates
  for synchronization.
- Every wait has a diagnostic timeout.
- Do not use arbitrary sleeps to coordinate scenarios.
- Give every scenario a unique workspace, host-control endpoint, and fake-provider endpoint so fixtures remain
  parallelizable.
- On teardown, request normal shutdown first. Force process termination only after a bounded timeout and report the
  captured protocol/provider/process state.

## Product APIs that tests must not require

The target architecture does not require:

- a test-specific `SquadApplication` constructor or factory;
- `SquadApplication.ViewModel` or `SquadApplication.Sessions`;
- public role, pending-interaction, transcript, or lifecycle collections;
- an event sink or lifecycle trace callback;
- optional collaborators that select test behavior;
- configurable capacities or timeouts used only to force test branches;
- `InternalsVisibleTo`;
- reflection over product internals;
- host-control commands for injecting UI or provider events; or
- a separately compiled test variant of either executable.

Once scenarios use the process boundary, these surfaces can be removed based on production call sites.

## Gherkin language

Every feature specifies behavior for one identifiable user: an operator, a role's agent, a configuration author, or
a UI-protocol client or provider implementer exercising a documented public contract. Choose that user first, then
write only in terms that user can know - the exact names published by the manual, CLI help, the configuration
schema, the visible dashboard, and supported protocols.

`BackendScenario`, a role-interaction scenario, a backend-spec fixture, the fake-provider fixture or its private
control pipe, a recording object, and any other test-support type are implementation details of the bindings. They
must never appear as a feature's actor or observable outcome. A feature devoted only to that test infrastructure
must be recast around the public behavior it enables, or removed if it has no independent user-facing contract.

### Canonical vocabulary modules

Each module below owns one coherent vocabulary, derived from `docs/manual/glossary.md`, the rest of the manual, CLI
help, the public configuration shape, and documented protocols. A binding class may implement several modules, but
it must not invent a second dialect for a module another binding class already owns, and a term is not user
language merely because it is technically precise or names a C# type.

| Module | User perspective and vocabulary |
| --- | --- |
| Project configuration | A configuration author configures roles, worktrees, models, permissions, and receive modes in `blaxquad/squad.json`. Role collections and records use tables. |
| Headquarters lifecycle | An operator launches, waits for, shuts down, or otherwise terminates Headquarters through `squad-hq`; process results and diagnostics remain explicit. |
| Dashboard operations | A user sends prompts, aborts work, and answers interactions for a role; the dashboard shows role state, usage, interactions, and transcript content. |
| Agent sessions | A provider implementer observes session start and disposal, receives role prompts or responses, and emits typed replies, idle, usage, interaction, failure, and tool events. The fake provider and its control transport stay behind these steps. |
| Transcript protocol | A UI-protocol client receives typed updates, synchronization, pages, archived entries, sequence positions, and truncation state. Ordered collections use tables. |
| Role commands | A role agent runs the documented `squad` context, handoff, `ready-for-next`, and `done-with-current` commands from its worktree. |
| Handoff delivery | An operator or role agent observes durable handoff fan-out, notification, retry, recovery, and queue state using glossary terms. |
| Public adapters | An operator selects the documented UI or provider adapter, while UI-protocol and provider-SPI users exercise their respective public contracts. |

### Choose the smallest form of variation

- Use a typed parameter (`{string}`, `{int}`, ...) when one scalar value varies.
- Use a data table for collections, records with optional fields, ordered event streams, or groups of related
  observations.
- Use a scenario outline when the same behavior is exercised across a matrix of examples.
- Use separate steps only when the behavior or externally observable meaning is actually different - never for
  singular versus plural wording, a boolean, a count, content length, or the presence of an optional field.

Replace comma-separated lists and string-encoded booleans with a table or typed conversion wherever the shape itself
communicates the contract. Do not replace many narrow, meaningful phrases with one opaque mega-step whose generic
table is a programming language in disguise.

Tables and parameter conversions must be strict: validate that a table declares only its required and supported
columns and rejects an unrecognized one, parse booleans and enums as typed values, preserve row order where it is
observable, and report malformed data clearly rather than silently ignoring or defaulting it.

### Compose, do not hard-code, orderings and races

For concurrent and failure scenarios, express reusable concepts - a pending operation, an independently started
command, a released operation, an action performed while another is pending - rather than one sentence that
hard-codes a specific pair of concurrent actions. Synchronization must stay deterministic through observable
acknowledgements and bounded waits, never sleeps.

### Organize bindings by language module, not by feature

Split responsibilities into binding classes named after the vocabulary they implement, not one class per feature.
Reqnroll resolves one scenario-scoped owner of the `BackendScenario` facade for every language module a scenario
uses: binding classes request `BackendScenario` (or another shared collaborator) through their constructor instead
of constructing their own, so Reqnroll's container creates and shares exactly one instance per scenario. Only one
binding class disposes a given process or resource it did not itself create an independent copy of.

Named concurrent projects and replacement launches remain explicit children of that same scenario-scoped owner
(for example, requesting a replacement backend process from the existing facade rather than constructing a second
one from scratch); binding classes must not construct independent default facades. Keep the facade and all
fake-provider plumbing - fixture selection, control-pipe wiring, and provider factories - in test support rather
than exposing them in Gherkin or adding production test APIs. Keep fixture selection in test setup when it is not
part of the behavior under specification: a scenario proving operator-facing lifecycle behavior must not name
"the fake provider" or "the echo provider" in its steps merely because a binding needs one internally.

Remove a phrase's binding as soon as no feature uses it any longer. A migration must not retain both the old and
the new wording for the same behavior.

### Examples

Prefer scenarios that cross a meaningful boundary, written from the chosen user's vocabulary:

```gherkin
Given `blaxquad/squad.json` configures:
  | role     |
  | coder    |
  | reviewer |

When the operator launches Headquarters
Then Headquarters starts an agent session for role "coder"

When the user sends "Review the change" to the architect
Then the architect agent receives "Review the change"

When the architect replies "The change is safe"
Then the architect transcript contains "The change is safe"
```

Avoid scenarios phrased in terms of implementation:

```gherkin
When an assistant event is enqueued on the view model
Then the role event count is 1
```

Technical protocol and failure cases may remain in the same `squad.Specs` project, but they still use the external
process/protocol boundary. Organizing features into folders does not create new production-aligned test layers or
assemblies.

## Architectural proof

Before migrating the full suite, prove one complete vertical path:

1. Publish `squad-hq` without `squad.AgentProvider.CopilotSdk`.
2. Start the actual executable with the stdio UI and the fake provider from `squad.AgentProvider.Fake.dll`.
3. Complete the real UI-ready handshake.
4. Let headquarters create a fake session for a configured role.
5. Send a prompt through the real versioned UI JSON protocol.
6. Observe that prompt through the fake-provider control pipe.
7. Emit an assistant reply from the fake provider.
8. Observe the resulting transcript protocol message.
9. Request shutdown through the real `squad-hq shutdown` command.
10. Confirm clean process exit and resource cleanup.

This vertical proof establishes the complete test architecture. Only after it works should existing white-box scenarios
be migrated and their test-driven product APIs removed.
