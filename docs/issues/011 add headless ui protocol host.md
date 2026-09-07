---
title: Add a headless UI protocol host
priority: 11
---

# Add a headless UI protocol host

This issue implements the [backend test strategy](../manual/test-strategy.md).

## Goal

Allow the actual `squad-hq` process to be driven through the real versioned UI JSON protocol without starting Photino
or Vue.

Add a headless `IWindowHost` implementation selected explicitly by headquarters. It must own the same
`UiProtocolSession` used by the visual host and transport newline-delimited JSON over standard input and standard
output.

This is a supported transport adapter, not a test hook. Photino remains the default UI.

## Implementation plan

### Slice 1: Add and expose the stdio protocol host

**Status: changes requested (e37278a940)**

1. Add a `squad.Stdio` adapter assembly beside `squad.Photino`. Implement one public `StdioWindowHost` that implements
   `IWindowHost`, depends only on the UI/hosting abstractions and `squad.Ui.Protocol`, and owns one
   `UiProtocolSession`. Keep newline framing, console ownership, and host lifecycle in this adapter; do not move
   transport concerns into `UiProtocolSession`, `SquadApplication`, or `SquadViewModel`.
2. On start, attach the normal UI event sources, begin one sequential standard-input read loop, pass every non-EOF
   line unchanged to `UiProtocolSession.ReceiveMessageAsync`, and wait for the existing `ui.ready` command before
   completing startup. Route `SessionsStartedAsync` directly to the session so initial snapshots and transcript
   synchronization use the same protocol delivery coordinator as Photino.
3. Serialize every outgoing protocol envelope as exactly one line on `Console.Out` and flush it before accepting that
   it was delivered. Keep writes coherent when snapshot, transcript, and command responses originate concurrently.
   The adapter must never write diagnostics or lifecycle text to stdout; existing command/process failures continue
   through `Console.Error`.
4. Give the host one idempotent lifecycle: EOF completes the close signal, host-control shutdown and process
   cancellation stop the input pump, and `StopAsync`/`DisposeAsync` detach event sources and dispose the protocol
   session exactly once. Startup must not hang if cancellation or EOF wins before `ui.ready`, and normal shutdown must
   not depend on another input line arriving.
5. Add a small headquarters `--ui <photino|stdio>` option parser. Accept the option at most once in any position
   supported by the existing launch parser, preserve `--continue`, `--provider`, and the optional workspace path,
   select `StdioWindowHost` only for the explicit `stdio` value, and keep Photino plus its current sleep-inhibitor
   behavior as the default. Report missing, repeated, or unknown UI values as concise `CliExitException` diagnostics
   on stderr.
6. Wire the new adapter project into the solution and `squad-hq` composition without adding a second UI SPI, a public
   message-injection API, a serialized-message callback, nullable stream/test collaborators, or headless branches in
   the application/domain layers. Do not make Photino optional in packaging in this issue.
7. Add focused black-box Gherkin scenarios in `squad.Specs` that launch the real published `squad-hq` with
   `--ui stdio` and an explicitly selected, minimal test-owned provider fixture. Exercise the real `ui.ready`
   handshake, initial snapshot/transcript messages, a command and resulting transcript update, page/synchronization
   requests, malformed protocol input, EOF, host-control shutdown, and stderr/stdout separation. Keep this support
   narrowly scoped; the reusable scenario facade and fake-provider control pipe belong to issue 012.
8. Add option-failure scenarios for missing, duplicate, and unknown `--ui` values, and retain coverage proving an
   omitted `--ui` still selects the existing Photino path. Run the focused headless/protocol/launch scenarios followed
   by the complete build and acceptance suite.

**Slice acceptance**

- `squad-hq launch --ui stdio` drives the real versioned `UiProtocolSession` over newline-delimited stdin/stdout and
  does not complete startup before `ui.ready`.
- State snapshots, transcript synchronization and updates, pages, command responses, and protocol errors are emitted
  through the existing protocol code paths as one JSON envelope per stdout line.
- EOF, host-control shutdown, cancellation, stop, and disposal converge on clean, idempotent host termination without
  blocked reads or leaked protocol subscriptions.
- Stdout contains protocol envelopes only; launch/runtime diagnostics remain on stderr.
- Omitting `--ui` preserves the current Photino host and protocol behavior.
- The production surface contains no test hook, optional test collaborator, message-injection method, or
  serialized-message callback for the headless transport.

#### Review findings on e37278a940

**Finding 1 — high**

- **Location:** `src/squad.Specs/Features/StdioUiProtocol.feature` (prompt and page/recovery scenarios),
  `src/squad.Specs/StepDefinitions/StdioUiProtocolSteps.cs` (`WhenTheUiSendsACommandForRoleWithPrompt`,
  `ThenATranscriptUpdateMessageForRoleIsWrittenToStdout`).
- **Violated behavior:** Slice 1 must exercise a command and the resulting transcript update through the real
  protocol. The scenario must fail if `prompt.send` is not delivered.
- **Root cause:** `SquadApplication` waits for `ui.ready` inside `WindowHost.StartAsync` and only then starts
  sessions. The stdio pump reads the next stdin line as soon as `ui.ready` is handled, so `prompt.send` is processed
  before `SessionRegistry` has a session. `SendAsync` fails with `Unknown role` and becomes `protocol.error`.
  `EchoAgentSession` later publishes `AgentStartedEvent`, which projects to a `transcript.update` ("Session started.").
  The Then step accepts any `transcript.update` for the role, so the scenario passes without the prompt being delivered.
- **Required outcome:** After `ui.ready`, wait for observable readiness that cannot occur before session admission,
  then send the prompt, and assert a `transcript.update` caused by that prompt (user text and/or echo reply). The
  scenario must fail if `prompt.send` is rejected as an unknown role. Apply the same sequencing to the page/recovery
  scenario if it depends on the prompt having landed.

**Finding 2 — medium**

- **Location:** `src/squad.Specs/Features/StdioUiProtocol.feature` (startup handshake scenario),
  `src/squad.Specs/StepDefinitions/StdioUiProtocolSteps.cs` (`ThenNoProtocolMessageIsWrittenToStdoutYet`).
- **Violated behavior:** Startup must not complete before `ui.ready`. This issue's test strategy requires waiting for
  semantic state with bounded deadlines rather than arbitrary sleeps.
- **Root cause:** Launch performs workspace, provider, and sleep-inhibitor work before `StdioWindowHost.StartAsync`.
  A 300ms sleep after process start can elapse entirely during that prep, so empty stdout does not prove the host is
  waiting for `ui.ready`. The scenario would still pass if the host published immediately on start.
- **Required outcome:** Lock the invariant without a fixed sleep that can expire before the host is listening. The
  scenario must fail if any protocol envelope is emitted before `ui.ready` is received, including when launch prep
  takes longer than a short sleep.

**Finding 3 — medium**

- **Location:** `src/squad.Specs/Features/StdioUiProtocol.feature` (host-controlled shutdown scenario),
  `src/squad.Specs/StepDefinitions/StdioUiProtocolSteps.cs`
  (`WhenSquadHqIsLaunchedWithUiStdioRequestingASmokeShutdownOnceReady`).
- **Violated behavior:** Slice 1 item 7 requires exercising host-control shutdown of the real published process.
- **Root cause:** The scenario sets `BLAXQUAD_PHOTINO_SMOKE=1`, which requests `StopAsync` from the `ui.ready`
  handler. That is the Photino smoke shortcut, not `squad-hq shutdown` / the host-control endpoint.
- **Required outcome:** After the stdio host is ready, request shutdown through the real host-control command and
  assert the process exits cleanly without closing stdin. Do not use the Photino smoke environment variable for this
  scenario.

## Acceptance criteria

- `squad-hq launch` can explicitly select a headless stdio UI mode.
- The headless host passes every input message through `UiProtocolSession`.
- Protocol output is written as one JSON message per stdout line.
- Process diagnostics are written to stderr and cannot corrupt the protocol stream.
- Startup waits for the normal UI-ready protocol command.
- Session-start notification, snapshots, transcript synchronization, updates, pages, and protocol errors use the same
  code paths as the Photino host.
- Host-control shutdown, cancellation, input closure, and disposal terminate the headless host cleanly.
- The default Photino launch path and protocol behavior are unchanged.
- No public message-injection method, serialized-message callback, or nullable test collaborator is added.
