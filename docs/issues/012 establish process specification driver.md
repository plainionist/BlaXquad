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

### Slice 1: Establish the provider-free process scenario facade

1. Add a dedicated backend-spec publication target to `squad.Specs` that publishes the real `squad-hq` with
   `IncludeCopilotSdkProvider=false` into its own test-output directory. Keep the existing production-like
   `squad-tools` publication unchanged for specifications that exercise the default Copilot package. The proof
   executable and dependency manifest must not reference or contain `squad.CopilotSdk`, its SDK dependency, or native
   runtime assets.
2. Introduce `BackendScenario` as the test-owned lifetime/composition root. Give workspace support semantic operations
   for creating a uniquely rooted configured Git project and locating role worktrees; give CLI support operations for
   running the exact published `squad` and provider-free `squad-hq` executables with captured exit code, stdout, and
   stderr. Do not resolve tools from `PATH`, launch against the checkout, or inspect ambient headquarters processes.
3. Extract a headless UI client that owns one launched process's standard-input writer, concurrent stdout/stderr
   readers, versioned envelope serialization, and the minimum protocol state required to wait for snapshots and
   transcript messages. Expose semantic commands and observations such as ready, send prompt, wait for role status,
   and wait for transcript content; do not expose raw JSON or product protocol DTOs to steps.
4. Make `BackendScenario.StartAsync` launch headquarters with `--ui stdio` and an explicitly supplied test provider,
   complete the real `ui.ready` handshake, and return only after the requested observable readiness condition.
   Expose lifecycle operations for normal shutdown through the real `squad-hq shutdown` command and clean process
   exit.
5. Centralize bounded waits and diagnostics. A timeout must report the command/process state plus captured stdout,
   stderr, and parsed UI state. Disposal requests normal host-control shutdown first, waits for a bounded interval,
   and only then terminates the exact scenario-owned child process. Preserve the primary scenario failure while
   appending cleanup diagnostics; do not use arbitrary synchronization sleeps.
6. Add a focused black-box Gherkin scenario that uses only the new facade to create a configured role, start the
   provider-free headquarters with the existing echo fixture, complete readiness, and shut down cleanly. Keep the
   existing stdio and provider-packaging specifications running unchanged while the new canonical driver is
   established.

**Slice acceptance**

- One user-oriented facade owns workspace, CLI, UI, and lifecycle capabilities without exposing child-process or
  protocol plumbing to its step definitions.
- The proof launches an exact, separately published `squad-hq` whose output and dependency manifest contain no
  `squad.CopilotSdk` reference or assets; normal production-like test publication still includes the default provider.
- The facade completes the real stdio ready handshake and requests shutdown through the actual host-control command.
- Every wait is bounded by observable process/protocol state and emits captured diagnostics on failure.
- Cleanup affects only the scenario-owned process and temporary project, even when another headquarters process is
  running.

### Slice 2: Add the fake provider channel and complete the vertical proof


1. Add one public fake `IAgentProviderFactory` in `squad.Specs` and provider-side backend, runtime, and session
   implementations in separate source files. Load that factory into the provider-free headquarters through the
   production `--provider` descriptor. Keep all fake behavior out of production assemblies and preserve the normal
   headquarters provider/runtime/session lifecycle.
2. Add a uniquely named, per-scenario local named-pipe endpoint and random token passed only through a test-owned
   environment variable. Headquarters must not parse, forward, or understand the channel. Use typed, versioned
   newline-delimited JSON messages with correlation identifiers so one reader can dispatch observations and command
   acknowledgements without competing reads.
3. Report provider observations for session start/disposal, prompts, harness messages, aborts, and interaction
   responses. Accept acknowledged commands that emit the provider-neutral event families needed by backend
   specifications, including assistant/reasoning/tool output, readiness and usage, interaction requests, idle,
   operation/session completion, and failures. Route by role and session identity and reject unknown commands or
   sessions with explicit diagnostics rather than silently acknowledging them.
4. Expose the test-process side as `scenario.Agent(role)` (or an equivalently narrow role controller) with semantic
   operations such as waiting for a prompt, replying, requesting an interaction, reporting usage, going idle, and
   failing or completing a session. Neither this API nor Gherkin steps may construct provider `AgentEvent` records or
   control-channel DTOs.
5. Integrate provider state into the facade's common bounded-wait and failure reporting. Startup must observe the
   configured role session through the control channel; teardown must close the channel, observe or report session
   disposal, request normal headquarters shutdown, and use bounded scenario-owned emergency cleanup if either side
   fails.
6. Add the complete black-box architectural proof in Gherkin: create a configured fake role, start the real
   provider-free headquarters, complete `ui.ready`, observe normal session startup, send a prompt through the real UI
   protocol, observe it through the fake-agent API, emit an assistant reply through that API, observe the real
   transcript protocol message, request shutdown through the real command, and confirm clean exit. Step definitions
   use only the scenario facade's semantic workspace, UI, agent, CLI, and lifecycle operations.
7. Run the focused process-driver proof and the complete acceptance suite. Existing specifications remain in
   `squad.Specs`; do not migrate or remove unrelated white-box scenarios as part of establishing this API.

**Slice acceptance**

- The fake provider and both ends of its private control channel live only in `squad.Specs`, implement the production
  provider SPI, and are loaded into the real provider-free headquarters process.
- A configured role starts through the normal lifecycle, and the controller observes its session without product test
  hooks.
- A prompt sent through the real UI JSON protocol reaches the semantic fake-agent API.
- A semantic assistant reply crosses the named pipe, becomes a provider-neutral event, and appears as a real
  transcript protocol message.
- Commands and observations have deterministic acknowledgements, bounded timeouts, role/session routing, and useful
  UI/provider/process diagnostics.
- Host-control shutdown exits cleanly, failed scenarios perform bounded cleanup, and no step definition sees raw
  provider events, protocol envelopes, pipe messages, child processes, or product objects.

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
