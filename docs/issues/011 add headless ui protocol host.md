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

## Release headquarters coexistence

Implementation and verification must work while an independently published Release `squad-hq` from the repository
`bin` directory is already running, including when that process hosts this checkout.

- Treat that process as unrelated ambient state and leave it running.
- Build and publish the candidate executable to the normal project/test output. Process specifications must launch the
  exact executable under the test output's `squad-tools` directory, never resolve `squad-hq` from `PATH` or use the
  repository `bin` directory.
- Do not run `install.ps1`, `install.sh`, or publish into the repository `bin` directory as part of implementation or
   verification; those paths belong to the independently running Release installation.
- Give every process specification a unique temporary project root. Never launch the candidate against this checkout
  and never send a shutdown command for this checkout.
- Do not enumerate, kill, reuse, or require the absence of other `squad-hq` processes. Host ownership and control are
  scoped by canonical project root, not by process name or executable location.
- Teardown may stop only the child process started by the scenario. The same specifications must pass whether or not
  an installed Release headquarters is running.

The separate output directories prevent build-time file contention; the separate project roots prevent host-lease
and host-control collisions.

## Scope boundary

This issue ends with the supported stdio host and focused process-level coverage of its transport and lifecycle.
Issue 012 owns the reusable backend scenario facade, behavioral fake provider, private provider control pipe, and the
complete prompt/reply vertical proof.

## Implementation plan

### Slice 1: Launch through the stdio ready handshake

**Status: complete (4e99ce9d8c)**

1. Add a `squad.Stdio` adapter assembly beside `squad.Photino`. Implement one public `StdioWindowHost` that implements
   `IWindowHost`, depends only on the UI/hosting abstractions and `squad.Ui.Protocol`, and owns one
   `UiProtocolSession`. Keep console transport concerns in this adapter.
2. Add a small headquarters `--ui <photino|stdio>` option parser. Accept the option at most once in any position
   supported by the existing launch parser, preserve `--continue`, `--provider`, and the optional workspace path,
   select `StdioWindowHost` only for the explicit `stdio` value, and keep Photino plus its current sleep-inhibitor
   behavior as the default. Report missing, repeated, or unknown UI values as concise `CliExitException` diagnostics
   on stderr.
3. Start one sequential standard-input read loop, pass each non-EOF line unchanged to
   `UiProtocolSession.ReceiveMessageAsync`, and wait for the existing `ui.ready` command before completing startup.
4. Wire the adapter into the solution and `squad-hq` composition without adding a second UI SPI, a public
   message-injection API, a serialized-message callback, nullable stream/test collaborators, or headless branches in
   the application/domain layers. Do not make Photino optional in packaging in this issue.

**Slice acceptance**

- A focused black-box Gherkin scenario launches the exact test-published `squad-hq` with `--ui stdio`, sends
  `ui.ready`, and observes that startup waits for that command.
- Missing, repeated, and unknown `--ui` values fail with concise stderr diagnostics; the option composes with every
  existing launch argument order that is already supported.
- Omitting `--ui` and specifying `--ui photino` both retain the existing Photino host and sleep-inhibitor behavior.
- The solution builds with the new adapter and the production surface gains no test hook or alternate UI abstraction.

### Slice 2: Carry the existing protocol over stdio

**Status: not started**

1. Attach the normal UI event sources when the host starts and route `SessionsStartedAsync` directly to the protocol
   session so initial snapshots and transcript synchronization use the same delivery coordinator as Photino.
2. Serialize every outgoing protocol envelope as exactly one newline-terminated line on `Console.Out` and flush it
   before accepting delivery. Serialize writes so snapshot, transcript, protocol-error, and command-response output
   cannot interleave.
3. Reserve stdout exclusively for protocol envelopes. Keep launch, lifecycle, provider, and command failures on
   `Console.Error`.
4. Add focused process scenarios using only the minimum test-owned provider fixture needed to let headquarters start.
   Cover the initial state, transcript synchronization, a page or synchronization request, and malformed protocol
   input. Do not add the behavioral fake provider or its control pipe here.

**Slice acceptance**

- Every non-EOF input line reaches the real versioned `UiProtocolSession` unchanged.
- Initial snapshots, transcript synchronization, requested protocol data, command responses, and protocol errors use
  the existing protocol paths and appear as one complete JSON envelope per stdout line.
- Concurrent publications cannot corrupt framing, and each accepted envelope has been flushed.
- Captured stdout contains protocol JSON only; diagnostics remain on stderr.

### Slice 3: Converge every termination path

**Status: not started**

1. Give the adapter one idempotent lifecycle. EOF completes its close signal; host-control shutdown and process
   cancellation stop the input pump; `StopAsync` and `DisposeAsync` detach event sources and dispose the protocol
   session exactly once.
2. Ensure cancellation or EOF before `ui.ready` unblocks startup, and ensure normal shutdown does not wait for another
   input line. Do not leave an unobserved read task or protocol subscription behind.
3. Add bounded process scenarios for EOF before and after readiness, host-control shutdown while stdin remains open,
   and cancellation. Track and clean up only the process created by the scenario.

**Slice acceptance**

- EOF, host-control shutdown, cancellation, stop, and disposal all terminate the headless host without a blocked read.
- Repeated or racing stop/dispose paths do not duplicate cleanup, lose the primary failure, or leak subscriptions.
- The process exits within a diagnostic timeout and releases its temporary project's host ownership.

### Slice 4: Harden the process contract and regressions

**Status: not started**

1. Keep the issue-specific process support narrow: resolve the executable by absolute path from the test
   `squad-tools` publication, create a unique temporary project, use bounded waits, and retain stdout/stderr/process
   state on failure. Leave the reusable facade and fake-provider channel to issue 012.
2. Add a coexistence regression that runs independently published headquarters executables against different
   temporary project roots and proves that stopping one fixture process does not stop or disturb the other. The test
   must create and own both fixture processes; it must never inspect or control an ambient installed process.
3. Retain coverage for the default Photino path and provider packaging. Confirm that the stdio addition does not make
   Photino optional or add a `squad.CopilotSdk` compile-time dependency to headquarters.
4. Run the focused headless/protocol/launch scenarios, then the complete build and acceptance suite. Leave any Release
   `squad-hq` that was already running from the repository `bin` directory untouched throughout verification.

**Slice acceptance**

- Headless process tests do not depend on `PATH`, the repository `bin` directory, the checkout as a project root, or
  the global absence of `squad-hq` processes.
- Headquarters instances from separate output directories coexist when they own different canonical project roots.
- Scenario cleanup affects only scenario-owned processes and project state.
- The focused checks, complete build, and acceptance suite pass while an independently running Release headquarters,
  when present, remains available.

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
- Build and acceptance work is isolated from an installed Release headquarters: it neither assumes that process is
   absent nor stops, reuses, or overwrites it.
