---
title: Establish the process-level backend specification driver
priority: 12
---

# Establish the process-level backend specification driver

This issue implements the [backend test strategy](../manual/test-strategy.md).

## Architecture

Keep the process specification boundary entirely inside `squad.Specs`. The reusable test API has four owners:

- `BackendScenario` owns one scenario lifetime and composes the other test-owned capabilities.
- Workspace/CLI support owns temporary project setup, exact published-tool selection, command results, and child
  processes.
- The headless UI client owns stdin/stdout protocol framing and the minimum received state needed for semantic waits.
- The fake-agent controller owns the private control endpoint and exposes role-oriented operations; the provider-side
  implementation owns the production SPI objects loaded into headquarters.

Step definitions depend only on these user-oriented capabilities. They do not serialize UI envelopes, inspect
provider event records, manage pipes or child processes, or reference product object graphs.

The architectural proof uses a dedicated test publication of `squad-hq` produced with
`IncludeCopilotSdkProvider=false`. Keep it separate from the normal `squad-tools` publication, which must continue to
contain the production default provider for existing specifications. Load the fake provider explicitly from
`squad.Specs.dll`; do not copy `squad.CopilotSdk` into the proof publication and do not add another test assembly.

## Implementation plan

Implement these slices in order. Keep exactly one slice in progress, and submit each slice for review before starting
the next one.

### Slice 1: Publish a provider-free headquarters for backend specifications

**Status: complete (6c195cc457)**

Add a dedicated `squad.Specs` publication target that publishes the real `squad-hq` with
`IncludeCopilotSdkProvider=false` into a separate test-output directory. Preserve the existing production-like
`squad-tools` publication unchanged. Add focused packaging coverage for both outputs.

**Slice acceptance**

- The backend-spec output contains the real `squad-hq` executable but no `squad.CopilotSdk` assembly, SDK dependency,
  native SDK asset, or dependency-manifest reference.
- The existing `squad-tools` output still contains the production default provider.
- Existing provider-packaging specifications pass without being weakened or replaced.

### Slice 2: Add exact published-tool and temporary-workspace support

**Status: complete (04650a621f)**

Add test-owned support that creates a uniquely rooted configured Git project, locates role worktrees, and runs the
exact published `squad` and provider-free `squad-hq` executables. Capture command line, exit code, stdout, stderr, and
process lifetime. Do not resolve tools from `PATH`, run binaries from the checkout, or inspect ambient headquarters
processes.

**Slice acceptance**

- A focused specification creates a configured temporary project through the support API and successfully invokes
  the exact backend-spec publication.
- Command failures report the executable, arguments, working directory, exit code, stdout, and stderr.
- All created paths and processes are scenario-owned and safe for parallel scenarios.

### Slice 3: Add the semantic headless UI client

**Status: complete (341dc3e8d4)**

Add a client for one launched `--ui stdio` process. It owns stdin, concurrent stdout/stderr collection,
newline-delimited versioned-envelope framing, and only the received state needed for semantic waits. Initially expose
readiness, prompt sending, role-status waiting, transcript waiting, and protocol-error reporting. Keep raw JSON and
product protocol DTOs private to the client.

**Slice acceptance**

- Focused black-box coverage completes the real `ui.ready` exchange against a published headquarters process.
- Reads and writes cannot deadlock because stdout and stderr are drained concurrently.
- Every semantic wait is bounded and reports captured process output plus parsed UI state on timeout.
- No step definition handles protocol envelopes, raw JSON, streams, or child processes.

### Slice 4: Compose startup and normal shutdown in `BackendScenario`

**Status: complete (a15c6acb0e)**

Introduce `BackendScenario` as the test-owned lifetime and composition root for workspace, CLI, UI, and process
support. `StartAsync` launches the provider-free headquarters with `--ui stdio` and an explicit existing test provider,
completes `ui.ready`, and returns only after observable readiness. Normal shutdown must use the real
`squad-hq shutdown` command and await clean process exit.

**Slice acceptance**

- A focused Gherkin scenario uses only `BackendScenario` semantic operations to create one configured role, start
  headquarters with the existing echo fixture, become ready, request host-control shutdown, and observe exit code
  zero.
- Step definitions do not expose paths beyond user-supplied inputs, process handles, protocol DTOs, or product object
  graphs.
- Existing stdio specifications continue to pass unchanged.

### Slice 5: Make scenario cleanup bounded, diagnostic, and isolated

**Status: complete (6cbfb7eb9e)**

Centralize bounded waits and diagnostics across workspace, CLI, UI, and lifecycle support. Disposal first requests
normal host-control shutdown, then waits for a bounded interval, and only then terminates the exact scenario-owned
child. Preserve the primary scenario failure while attaching cleanup diagnostics. Do not add arbitrary synchronization
sleeps.

**Slice acceptance**

- Focused coverage proves timeout diagnostics include command/process state, stdout, stderr, and parsed UI state.
- Focused coverage proves emergency cleanup targets only the process launched by that scenario, even while another
  headquarters process is running.
- Temporary workspace cleanup is bounded and a cleanup failure cannot replace the original scenario failure.

### Slice 6: Load a minimal fake provider through the production SPI

Add one public fake `IAgentProviderFactory` to `squad.Specs`, with provider-side backend, runtime, and session
implementations in separate files. Load it into the provider-free headquarters through the production `--provider`
descriptor. At this stage the fake only needs to establish and dispose a configured role session through the normal
provider lifecycle.

**Slice acceptance**

- The published provider-free headquarters loads the factory from `squad.Specs.dll` by explicit descriptor and starts
  a configured role session.
- Session creation and disposal use the production provider/runtime/session lifecycle.
- All fake behavior remains in `squad.Specs`; no product test hook, extra test assembly, or `squad.CopilotSdk`
  dependency is introduced.

### Slice 7: Establish the private fake-provider control transport

Add the test-runner and provider-process ends of a uniquely named local named pipe. Pass its endpoint and random
per-scenario token only through a test-owned environment variable. Use typed, versioned, newline-delimited JSON
messages with correlation identifiers and one dispatching reader per endpoint. Initially support authenticated
connection, session-started observation, session-disposed observation, command acknowledgement, and explicit protocol
errors.

**Slice acceptance**

- A focused process specification observes session start and disposal across the pipe.
- Invalid tokens, message versions, correlation identifiers, and unknown commands fail with explicit diagnostics.
- Concurrent observations and acknowledgements cannot compete for reads.
- Headquarters does not parse, forward, or otherwise know about the control channel.

### Slice 8: Expose prompts and assistant replies through a role controller

Extend the control protocol to report received prompts and to accept an acknowledged semantic assistant-reply command.
Route all traffic by role and session identity and reject unknown or disposed sessions. Expose the test-process API as
`scenario.Agent(role)` (or an equivalently narrow controller) with `WaitForPromptAsync` and `ReplyAsync`; keep control
DTOs and provider `AgentEvent` values behind that API.

**Slice acceptance**

- A prompt sent through the real UI protocol is observed through the semantic role controller.
- A semantic reply crosses the pipe, is translated to the production provider-neutral assistant event, and is
  acknowledged deterministically.
- Unknown roles, sessions, commands, and replies after disposal produce bounded, actionable failures.
- Gherkin steps construct neither control messages nor provider events.

### Slice 9: Complete the vertical architectural proof

Add one black-box Gherkin scenario that creates a configured fake role, starts the real provider-free headquarters,
completes `ui.ready`, observes the role session, sends a prompt through the real UI protocol, observes it through the
role controller, emits an assistant reply, observes the resulting real transcript message, requests shutdown through
the real command, and confirms clean exit.

**Slice acceptance**

- The scenario crosses every required process and protocol boundary without accessing a product object graph.
- Its step definitions use only semantic workspace, UI, agent, CLI, and lifecycle operations from the scenario facade.
- The proof has no arbitrary sleeps and all waits produce combined process, UI, and provider diagnostics.
- The focused process-driver proof passes repeatedly and remains safe to run in parallel.

### Slice 10: Complete the reusable fake-agent event surface

Extend the private channel and semantic role controller with the remaining event families required by the documented
backend test API: harness messages, aborts, interaction responses, reasoning/tool output, readiness, usage,
interaction requests, idle, operation/session completion, and failures. Add focused black-box scenarios only for
meaningful supported behavior needed to prove each family and its acknowledgement/routing semantics.

**Slice acceptance**

- Each supported observation and command has a typed semantic role-controller operation, deterministic
  acknowledgement, bounded timeout, and role/session routing.
- Provider state is included in common scenario diagnostics and teardown reports missing session disposal.
- No public test API or step definition exposes provider events, pipe messages, child processes, or product objects.
- The complete existing acceptance suite passes; unrelated white-box scenarios are not migrated or removed in this
  issue.

## Goal

Prove the complete target architecture and establish the single test-owned API that later specification migrations
will use.

Keep all support in `squad.Specs`. Add:

- a scenario driver for the real `squad` and `squad-hq` processes;
- a headless UI JSON client;
- a fake provider implementing the production provider SPI; and
- a private named-pipe control channel between the test runner and the fake provider loaded in the headquarters
  process.

The driver exposes user-oriented workspace, CLI, UI, agent, and lifecycle operations. Step definitions must not see
provider event records, protocol plumbing, child-process details, or product objects.

## Acceptance criteria

- The fake provider and all of its control-channel code live in `squad.Specs`; no test assembly is added.
- The fake provider is loaded into the real `squad-hq` process through the provider SPI.
- The published headquarters process used by the proof neither references nor contains `squad.CopilotSdk`.
- The driver completes the real UI-ready handshake over stdin/stdout.
- A configured fake role session starts through the normal headquarters lifecycle.
- A prompt sent through the UI JSON protocol reaches the fake provider.
- An assistant reply emitted through the fake-provider control channel appears as a real transcript protocol message.
- Shutdown is requested through the actual `squad-hq shutdown` command and the host exits cleanly.
- All waits use observable acknowledgements or state with bounded diagnostic timeouts; no arbitrary synchronization
  sleeps are introduced.
- A failed scenario performs bounded cleanup and reports captured process, UI, and provider state.
- Existing specifications remain in `squad.Specs` and continue to run while migration is in progress.
