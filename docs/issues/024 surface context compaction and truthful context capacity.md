---
title: surface context compaction and refresh current usage
priority: 1
---

# Surface context compaction and refresh current usage

## Symptom

During a long-running role, the context meter was observed first at `237k/264k` and then at `266k/264k`, without any
compaction message in the transcript. When the role became idle, the meter dropped to `50k/264k`.

Copilot CLI shows a transcript message while context compaction is in progress. BlaXquad should provide the same
feedback. Without it, these materially different situations look identical:

- automatic compaction is running normally in the background; or
- compaction never started, failed, or completed without the UI receiving a fresh context snapshot.

The label can also report more used tokens than its supposed limit while the bar is silently clamped to 100 percent.

## Analysis result

Automatic compaction is working in the observed session, and the displayed 264k complete context-window limit is
correct. The defects are that BlaXquad does not project the compaction lifecycle and does not promptly replace the
pre-compaction current-usage value after compaction completes.

The active coder session's persisted SDK events contained three successful threshold-triggered compactions. The
latest one recorded:

- `session.compaction_start` at 160,291 tokens with `tokenLimit: 200000` and `trigger: threshold`;
- `session.compaction_complete` 106 seconds later with `success: true`;
- 38,290 post-compaction tokens;
- 122,005 tokens and 146 messages removed.

The start value is consistent with the SDK's default background threshold of 80 percent of its 200k prompt limit.
The earlier successful compactions started at 162,408 and 168,234 tokens and reduced the context to 49,103 and
62,514 tokens respectively. The runtime therefore has repeatedly demonstrated the intended behavior in this run.

The observed fall to about 50k when the role became idle is also consistent with the latest successful compaction
plus subsequent conversation. It is not evidence that the transcript itself was erased; compaction replaces old
model context with a generated summary while the SDK event history remains persisted.

## Root causes

### Compaction lifecycle events are discarded

`CopilotSdkClient` sends every SDK event through `PublishEvent`, but its switch has no cases for
`session.compaction_start` or `session.compaction_complete`. They reach the default branch and are only written to
`Debug`, so no provider-neutral event reaches `AgentEventProjector` and no transcript entry can be produced.

`RawSdkEventTrace` does not close this diagnostic gap. Although it is called for every event, `TryDescribe` accepts
only tool events. Enabling `BLAXQUAD_SDK_EVENT_TRACE` therefore still does not record compaction start, success,
failure, trigger, or token deltas.

The existing ViewModel scenario that injects the synthetic system message `Context compaction started` proves only
that an already-translated system message renders. It does not prove that a real SDK compaction event is translated.

### The complete context-window limit is correct

`CopilotSdkRuntimeSession.CalculateContextLimit` deliberately combines the model's maximum prompt tokens and maximum
output tokens. For the observed session this correctly produces the complete 264k context window from a 200k prompt
limit plus a 64k maximum output allowance.

This calculation took deliberate investigation and is not part of the defect. The issue must not replace it with the
prompt limit, reinterpret 264k as an error, or change the denominator shown by the meter.

Only the current-usage numerator is wrong or stale after compaction. `GetContextUsageAsync` obtains it from
`ContextAttribution.TotalTokens`; after a successful compaction, the application must obtain and publish the new
current value while preserving the existing complete context-window limit.

### Active reconciliation is not tied to compaction completion

Context usage is refreshed on a fixed five-second, activity-driven schedule and once more on idle. Normal refresh
requests are dropped while another refresh is in flight, refresh failures are swallowed, and compaction completion
does not request a distinguished immediate refresh that survives an in-flight refresh.

The observation that the meter retained a very high value while working and reconciled to about 50k on idle is
therefore possible even though compaction succeeded. Current telemetry cannot distinguish a legitimately growing
pre-compaction snapshot from a stale snapshot retained after compaction.

`RoleHeader.vue` clamps only the fill width to 100 percent while rendering the stale raw `used/limit` label. This is
how the UI can show `266k/264k` until a later refresh supplies the post-compaction current usage. The 264k denominator
must remain unchanged.

## Required change

Keep context policy and compaction authoritative in the Copilot runtime. BlaXquad should project the runtime's state,
not implement a second compaction policy.

1. Add provider-neutral compaction-started and compaction-completed events. Preserve the trigger, success/failure,
   token limit, pre/post token counts, tokens/messages removed, and error text. Do not propagate the generated
   summary content into diagnostics or the UI.
2. Translate the SDK's real compaction lifecycle events in `CopilotSdkClient`.
3. Match Copilot CLI's transcript behavior: when compaction starts, append a visible `Compacting context...` system
   entry immediately. Resolve that entry to a concise success or failure result when completion arrives, rather than
  leaving an unexplained permanent in-progress message.
4. Preserve `CalculateContextLimit` and the complete context-window value it produces. Do not change the meter's
  denominator from 264k to the SDK's 200k prompt limit.
5. Trigger a mandatory context-usage refresh after compaction completes. If another refresh is in flight, retain the
  request and run it immediately afterwards, as the existing final-idle refresh does. A successful completion must
  replace the stale numerator without waiting for `SessionIdleEvent`.
6. If that refresh fails, retain the last known values and retry on subsequent activity or idle. Record enough
  diagnostics to distinguish refresh failure from compaction failure without changing the displayed limit.
7. Extend the opt-in raw SDK trace with scalar compaction lifecycle data. Never trace `summaryContent`, which can
   contain the compacted conversation.

## Acceptance scenarios

- Given a real provider compaction-start event, the role transcript immediately gains one system entry containing
  `Compacting context`, matching the feedback available in Copilot CLI.
- Given a successful completion from 160,291 to 38,290 tokens, the in-progress transcript entry resolves to a
  completion message exactly once, and a mandatory refresh replaces the pre-compaction current usage without waiting
  for `SessionIdleEvent`.
- In that scenario, a meter showing `160k/264k` before compaction shows the refreshed current value against the same
  `264k` complete context-window limit afterwards. The fix does not substitute the 200k prompt limit.
- If a normal usage refresh is already in flight when completion arrives, the completion refresh remains pending and
  runs once immediately after the in-flight refresh. It is not dropped.
- Given a failed completion, the in-progress transcript entry exposes the failure without terminating an otherwise
  usable agent session or changing the complete context-window limit.
- A stale `266k/264k` observation is replaced after successful compaction without requiring the role to become idle;
  both the meter width and label remain stable and readable during the transition.
- Compaction summary content is absent from the application transcript, protocol messages, and
  `BLAXQUAD_SDK_EVENT_TRACE` output.

Cover provider-event translation and context-state projection in the black-box Gherkin suite. Add a focused
Playwright scenario for the transcript message and the meter's post-compaction update. Retain the active usage
refresh scenario, but extend it to prove that completion forces reconciliation before idle and preserves the
complete context-window denominator.

## Non-goals

- Reimplementing or manually scheduling the Copilot runtime's automatic compaction.
- Changing the SDK's default 80-percent background or 95-percent blocking thresholds.
- Changing, renaming, or replacing the computed complete context-window limit shown by the meter.
- Displaying or persisting the generated compaction summary in BlaXquad's transcript.