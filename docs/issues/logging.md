---
title: logging
priority: 40
---

- all errors or warnings need to be written to a log with date and time
- place log files under .blaxquad/logs/
- one file per session

- use Microsoft.Extensions.Logging.Abstractions so that main code does not depend on particular logging framework
- then choose an appropriate logging impl

- i saw the HQ crashing once - make esp. sure that we have last chance exception handler which logs any "fallen through" exception.

- even with logging introduced keep existing UI error message

- every error discovered in the backend also needs to be logged before shown in the UI

- as of now we dont need verbose info or debug logging
- only add key information which might be necessary to analyse a crash

- consider logging as a cross-cutting concern. 

- this is about backend only!
- this issue does not require any new tests or scenarios

## Review decisions

The plan deliberately makes the following interpretations so that logging does not become a new subsystem:

- A **session** is one successful `squad-hq launch` process lifetime, not one agent-provider session per member. One
	launch therefore owns one log file even when it starts several member sessions.
- Logging covers the long-running Headquarters backend. The `squad` role CLI, the Vue client, and the short-lived
	`squad-hq shutdown` and `wait-for-agent` clients stay out of scope.
- Use `Microsoft.Extensions.Logging` contracts in backend code and Serilog with its file sink only in the
	`squad-hq` composition root. Do not add a `squad.Logging` assembly or a project-specific logging abstraction.
- Write plain-text diagnostic records containing a UTC timestamp, severity, category, message, and full exception
	details. Configure `Warning` as the minimum level; do not add routine lifecycle, prompt, transcript, tool payload,
	debug, or trace records.
- Name each file from the launch's UTC start time plus process ID and place it under
	`<project-root>/.blaxquad/logs/`. Do not add rotation, retention, compression, upload, or runtime configuration.
- Preserve every existing UI error, standard-error message, exit code, and cancellation behavior. Logging is an
	additional diagnostic side effect only.
- Record an unexpected failure once, where it becomes terminal, is converted into a user-visible error, or is
	deliberately recovered so processing can continue. Do not duplicate exceptions at intermediate catch/aggregate/
	rethrow sites.
- Expected cancellation, defensive catches whose failure is still propagated to one of those owning boundaries,
	best-effort usage refresh, and the opt-in raw SDK event trace are not production warning/error records. Keep the
	raw trace independent when `BLAXQUAD_SDK_EVENT_TRACE` is explicitly enabled.
- Do not add a custom exception type for logging; no caller needs a new distinguishable failure across an API
	boundary.

This issue has an explicit exception from `docs/manual/test-strategy.md`: do not add or modify tests or scenarios.
The coder must still build the solution and run the existing affected Gherkin features after each slice, followed by
the full backend acceptance suite after the final slice.
This issue has an explicit exception from `docs/manual/test-strategy.md`: do not add or modify tests or scenarios.
The coder must still build the solution and run the existing affected Gherkin features after each slice, followed by
the full backend acceptance suite after the final slice.

## Design

`squad-hq launch` creates and disposes one `ILoggerFactory` after resolving the project root and acquiring the
Headquarters lease. The concrete Serilog setup remains in that composition root. The factory is passed through the
existing runtime and hosting composition paths; consumers request typed `ILogger<T>` instances and depend only on
`Microsoft.Extensions.Logging.Abstractions`.

Four existing boundaries need contextual logging:

1. `Launch` records fatal startup, runtime, cleanup, and otherwise unhandled process failures before preserving the
	 current command-line result.
2. `SessionGeneration` records provider error events and unexpected event/session termination before those failures
	 become member error state visible in the dashboard.
3. `HandoffDeliveryService` records recoverable delivery, archival, and recipient-notification failures before its
	scan continues.
4. The existing backend UI-error publication path records an error before sending the message that opens the
	dismissible error alert. Member-specific error alerts are already covered by `SessionGeneration` and are not
	logged a second time.

Expected shutdown/cancellation is not a warning or error. Log messages include stable context such as member ID,
but never serialized UI messages, prompts, transcript text, interaction answers, environment variables, or other
user payloads.

## Slice 1: Per-launch file and last-chance Headquarters failures

**Logical change:** A Headquarters launch owns one backend log file and records every failure that escapes the
long-running process boundary.

Implementation:

- Add the concrete Serilog logging bridge and file sink only to `squad-hq`; add
	`Microsoft.Extensions.Logging.Abstractions` only to modules that consume its contracts.
- Resolve `ProjectLayout`, acquire the Headquarters lease, and then create one log file at the agreed path before
	loading provider/hosting plug-ins or preparing the workspace. A failed competing launch does not create a second
	session log.
- Keep one logger factory alive for the complete launch and flush/dispose it during final cleanup.
- Add the launch boundary's last-chance exception handling, including `AppDomain.UnhandledException` and
	`TaskScheduler.UnobservedTaskException` callbacks for failures outside the awaited main task. Record the original
	exception with stack and inner exceptions before retaining today's stderr text and exit code, and unregister the
	callbacks when the launch ends. Do not log normal Ctrl+C or requested shutdown.

Acceptance criteria:

- One successful launch creates exactly one file under `.blaxquad/logs`; a second launch after shutdown creates a
	distinct second file.
- A failure escaping launch is present in that launch's file with UTC date/time, severity, source category, original
	message, and exception details.
- The same failure still produces the pre-existing standard-error text and exit code.
- A clean shutdown produces no warning/error record, and a rejected competing launch creates no extra log file.

**Status:** complete in 8721ff8c0e. Review findings on f8ba27ba17 are resolved (no test/scenario changes;
diagnostic log documented).

## Slice 2: Provider and member-session errors before UI projection

**Logical change:** Provider failures that become a member's visible error state are recorded with member context
without moving logging into application or domain state.

Implementation:

- Pass the launch-owned logger factory through `Headquarters` and `SquadRuntime` to `SessionGeneration`.
- Log an `AgentErrorEvent` before enqueueing it for projection, including the member ID and provider message.
- Log an unexpected provider event-stream or session-completion exception before it is propagated or passed to
	`MarkRoleFailedAsync`. In particular, record an event-stream exception before either the existing early return or
	rethrow when session completion is observed. Continue to ignore expected teardown cancellation.
- Do not log ordinary provider events, prompts, transcripts, usage values, or successful lifecycle transitions.

Acceptance criteria:

- A provider error event is logged with its member ID before the unchanged error reaches that member's dashboard
	state/transcript.
- A failed member session is logged with its member ID and full exception while Headquarters and unaffected members
	continue running exactly as today.
- Expected session cancellation during Headquarters shutdown adds no warning/error record.

**Status:** complete in f7952d8710.

## Slice 3: Recoverable handoff delivery failures

**Logical change:** Handoff failures that are deliberately isolated so later files can still be delivered move into
the launch's diagnostic log instead of a separate fixed-purpose log.

Implementation:

- Pass an abstraction logger from `SquadRuntime` into `InProcessHandoffPoller` and `HandoffDeliveryService`.
- Record a delivery or failed-archive exception as an error with the handoff path and full exception; record a
	recipient notification failure as a warning with member and handoff context before continuing.
- Remove `HandoffDeliveryLog` and its `HandoffLog` path plumbing from workspace preparation after its failure records
	have moved to the session log. Do not carry its successful `delivered` entries forward because this issue excludes
	routine informational logging.
- Preserve handoff delivery, retry, failed-artifact archival, and recipient notification behavior.

Acceptance criteria:

- A failed handoff delivery or failed archival is present in the current launch log with its path and exception,
	while the scan continues and existing failed-artifact behavior is unchanged.
- A notification failure is logged as a warning with recipient context without undoing the completed delivery.
- A successful delivery adds no warning/error entry.

**Status:** complete in 2720b67245. Slice 4 remains.

## Slice 4: Errors shown in the existing UI alert

**Logical change:** Every backend error that opens an existing dismissible error alert in the UI is recorded before
that error is sent to the UI.

Implementation:

- Carry the launch-owned `ILoggerFactory` in `HostingContext`; each hosting adapter creates and passes a typed
	`ILogger<UiProtocolSession>` when constructing its existing protocol session.
- Route every existing backend publication that opens the general UI error alert through one `UiProtocolSession`
	helper. That helper logs the same error at `Error` level immediately before sending it to the UI, including the
	original exception when one exists.
- Keep member-operation failures in `squad.Application` free of logging dependencies: their completion tasks already
	propagate to this boundary, which records them before showing the existing alert.
- Do not log the serialized request or any additional UI payload, and do not add new error categories, UI behavior,
	or client-side logging.
- Keep the existing error message, dismiss behavior, transport envelope, correlation, and hosting plug-in selection
	unchanged.

Acceptance criteria:

- A representative backend failure that opens the general UI error alert is present in the current launch log with
	the same message before the unchanged alert appears.
- A provider/member failure remains visible in its existing role error alert and is logged once through Slice 2,
	not again while snapshots are delivered.

## Slice order and completion

Implement and integrate the slices in order: Slice 1 establishes the process-owned logger, Slice 2 extends it along
the provider-session path, Slice 3 replaces the separate handoff failure log, and Slice 4 extends logging across the
hosting plug-in boundary. After each slice, build `squad.slnx` and run the existing affected Gherkin features; run
the full backend acceptance suite after Slice 4. Each slice includes its own implementation and directly affected
manual update, and leaves a buildable, releasable intermediate state.
hosting plug-in boundary. After each slice, build `squad.slnx` and run the existing affected Gherkin features; run
the full backend acceptance suite after Slice 4. Each slice includes its own implementation and directly affected
manual update, and leaves a buildable, releasable intermediate state.

The issue is complete when all four error boundaries write to the same per-launch file, existing user-visible error
behavior is unchanged, the focused and full backend suites pass, and no logging dependency has entered
`squad.Domain`, `squad.Application`, `squad.Ui.Abstractions`, or the Vue client.
