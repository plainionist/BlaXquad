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

The main backend suite must not reference or load `squad.CopilotSdk`.

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
  |     `-- fake provider loaded from squad.Specs.dll
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

All feature files, step definitions, the scenario driver, the fake provider, and test support remain in
`squad.Specs`.

Internal folders and helper types may separate responsibilities inside that project, but those are test implementation
details. They must not become new assemblies or reproduce the product module boundaries.

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
- Emit provider-neutral assistant, reasoning, tool, readiness, usage, interaction, and failure events.
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
without `squad.CopilotSdk`, provider selection must happen at process composition.

Headquarters loads a provider factory selected explicitly by a trusted command-line option or installation manifest:

- the production package selects the Copilot SDK provider by default;
- backend specs explicitly select the fake provider from `squad.Specs.dll`; and
- no provider assembly is discovered or loaded from the target workspace.

Loading arbitrary code from a repository configuration is prohibited. An alternate provider path must be an explicit
choice by the process launcher.

The provider-neutral headquarters executable must not have a compile-time dependency on `squad.CopilotSdk`. Production
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

## Scenario design

Prefer scenarios that cross a meaningful boundary:

```gherkin
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

1. Publish `squad-hq` without `squad.CopilotSdk`.
2. Start the actual executable with the stdio UI and the fake provider from `squad.Specs.dll`.
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
