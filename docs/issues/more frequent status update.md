---
title: refresh usage during active agent sessions
priority: 50
---

# Refresh usage during active agent sessions

## Problem

The role header shows current context-window usage and accumulated AI credits (AIC), but long-running Copilot turns can
leave both values stale until the session becomes idle.

The Copilot adapter currently requests both values when a session is attached and after a session-idle event. AIC can
also advance when the SDK emits a usage checkpoint, but that does not provide a dependable refresh cadence during a
long turn.

Refreshing after every fixed number of raw SDK events is not suitable. Assistant and reasoning deltas can arrive in
large bursts, so five events may occur within milliseconds, while a long-running operation may produce no events for
an extended period.

## Goal

Keep context and AIC information reasonably current while a Copilot session is active without creating unbounded or
idle-time polling load.

## Decision

Use a time-based, event-aware throttle for each active Copilot session:

- SDK activity marks usage as needing refresh and ensures a refresh is scheduled.
- Run at most one active usage refresh cycle per five-second window for each session.
- One cycle may request both context attribution and AIC metrics, but neither request may overlap a prior request for
  the same metric.
- Coalesce all activity received during a window into the next refresh rather than counting raw events.
- Keep the immediate initial refresh when a session is attached.
- Keep SDK usage-checkpoint events as immediate AIC updates without requiring another metrics request.
- On idle, stop active refresh scheduling and perform one final refresh of both values.
- If the idle refresh arrives while a request is in flight, retain a pending final refresh and run it after the
  in-flight request completes; do not silently discard it.
- Do not schedule refreshes while a session remains idle.
- Stop pending refresh work when the session is disposed or fails.
- Treat a transient metrics failure as non-fatal and allow later activity or idle to retry.

Five seconds is a fixed implementation policy for this change. Do not add user configuration or a general-purpose
scheduler abstraction.

## Scope

Keep the refresh policy inside the Copilot provider adapter, where the SDK RPCs and event stream are owned. Continue to
publish the existing provider-neutral context-usage and session-usage events.

Do not change the UI protocol, move refresh authority into Vue, or add client-side estimation. The existing C# state
projection remains authoritative, and the existing role-header presentation should update from normal snapshots.

## Acceptance criteria

- During a Copilot turn lasting longer than one refresh window, newly available context and AIC values can reach the
  UI before the session idles.
- Active refresh traffic is bounded to at most one context request and one AIC metrics request per session per
  five-second window, excluding the immediate attach and final idle refreshes.
- Bursts of assistant, reasoning, and tool events do not increase the refresh rate.
- Idle sessions generate no periodic usage requests.
- An idle transition publishes the latest values even when it races with an active refresh.
- AIC usage checkpoints continue to update AIC immediately and do not regress accumulated usage.
- Refreshes for different role sessions are independent.
- Refresh failure, cancellation, disposal, and session failure leave no background loop or unobserved exception and do
  not fail an otherwise healthy agent session.
- No UI protocol or frontend changes are required.

## Test strategy

Follow the [backend test strategy](../manual/test-strategy.md).

- Extend the black-box Gherkin suite in `src/squad.Specs` only where needed to establish the supported observable
  contract: provider-reported context and AIC usage received while a role is still working appears in the real UI JSON
  protocol before idle, and final usage remains correct after idle.
- Drive `squad-hq` through the stdio UI protocol and control usage through the existing fake-provider channel. Use
  semantic state waits with bounded diagnostic deadlines; do not coordinate the scenario with arbitrary sleeps.
- Do not reference or load `squad.CopilotSdk` from the backend suite, directly construct `CopilotSdkAgentSession`,
  expose refresh counters or callbacks, or add production clock/timing seams solely for tests.
- Do not assert internal timer ticks, RPC callback counts, or exact helper-call order in Gherkin. The externally
  supported contract is that active usage becomes visible before idle and final usage is preserved.
- Verify the provider-specific five-second cadence with a real Copilot smoke run and diagnostics during implementation.
  Do not create a white-box SDK test harness to automate that implementation detail.
- Add or change Playwright coverage only if presentation behavior changes; this issue is expected to require no Vue
  change.