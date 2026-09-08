---
title: refresh current context usage after compaction
priority: 1
---

# Refresh current context usage after compaction

## Symptom

During a long-running role, the context meter was observed first at `237k/264k` and then at `266k/264k`. Although
automatic compaction completed successfully, the meter did not drop to `50k/264k` until the role became idle.

The label can also report more used tokens than its supposed limit while the bar is silently clamped to 100 percent.

## Analysis result

Automatic compaction is working in the observed session, and the displayed 264k complete context-window limit is
correct. The defect is that BlaXquad does not promptly replace the pre-compaction current-usage value after
compaction completes.

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
plus subsequent conversation.

## Root causes

### Compaction diagnostics are absent

`RawSdkEventTrace` does not close this diagnostic gap. Although it is called for every event, `TryDescribe` accepts
only tool events. Enabling `BLAXQUAD_SDK_EVENT_TRACE` therefore still does not record compaction start, success,
failure, trigger, or token deltas.

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

1. Preserve `CalculateContextLimit` and the complete context-window value it produces. Do not change the meter's
   denominator from 264k to the SDK's 200k prompt limit.
2. Trigger a mandatory context-usage refresh after a successful `session.compaction_complete`. If another refresh is
   in flight, retain the request and run it immediately afterwards, as the existing final-idle refresh does. The
   completion must replace the stale numerator without waiting for `SessionIdleEvent`.
3. If that refresh fails, retain the last known values and retry on subsequent activity or idle. Record enough
   diagnostics to distinguish refresh failure from compaction failure without changing the displayed limit.
4. Extend the opt-in raw SDK trace with scalar compaction lifecycle data: trigger, success/failure, token limit,
   pre/post token counts, tokens/messages removed, and error text. Never trace `summaryContent`, which can contain the
   compacted conversation.

## Acceptance scenarios

- Given a successful completion from 160,291 to 38,290 tokens, a mandatory refresh replaces the pre-compaction
  current usage without waiting for `SessionIdleEvent`.
- In that scenario, a meter showing `160k/264k` before compaction shows the refreshed current value against the same
  `264k` complete context-window limit afterwards. The fix does not substitute the 200k prompt limit.
- If a normal usage refresh is already in flight when completion arrives, the completion refresh remains pending and
  runs once immediately after the in-flight refresh. It is not dropped.
- A failed completion remains distinguishable from a context-refresh failure without terminating an otherwise usable
  agent session or changing the complete context-window limit.
- A stale `266k/264k` observation is replaced after successful compaction without requiring the role to become idle;
  both the meter width and label remain stable and readable during the transition.
- Compaction summary content is absent from `BLAXQUAD_SDK_EVENT_TRACE` output.

Cover completion-driven context-state reconciliation in the black-box Gherkin suite. Add a focused Playwright
scenario for the meter's post-compaction update. Retain the active usage refresh scenario, but extend it to prove
that completion forces reconciliation before idle and preserves the complete context-window denominator.

## Non-goals

- Reimplementing or manually scheduling the Copilot runtime's automatic compaction.
- Changing the SDK's default 80-percent background or 95-percent blocking thresholds.
- Changing, renaming, or replacing the computed complete context-window limit shown by the meter.
- Displaying or persisting the generated compaction summary.