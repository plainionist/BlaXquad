---
title: prompt history
priority: 75
---

## Goal

Allow an operator to recall and resend previously submitted prompts from a role's
prompt composer with the Up and Down arrow keys, similar to Copilot CLI.

## Scope

- Implement this entirely in `squad-ui`; do not change the UI protocol or C# backend.
- Keep history in memory for the lifetime of the current dashboard only. Do not persist it through dashboard reloads or Headquarters restarts.
- Maintain an independent history for each role.
- Retain at most the 50 most recently submitted prompts per role.
- Record the trimmed prompt carried by every `prompt.send` command. Blank prompts are not recorded.

## Keyboard behavior

- With an empty composer, unmodified `ArrowUp` recalls the most recently submitted prompt for that role.
- With a non-empty composer, history navigation starts only when the selection is collapsed and the caret is at the absolute beginning. Otherwise `ArrowUp` keeps its normal textarea behavior so multiline prompts remain editable.
- When history navigation starts, preserve the current unsent draft.
- While navigating, repeated `ArrowUp` moves toward older prompts and `ArrowDown` moves toward newer prompts. Navigation stops at either end and does not wrap.
- Moving past the newest history entry with `ArrowDown` restores the preserved unsent draft and leaves history-navigation mode.
- Editing a recalled prompt leaves history-navigation mode and treats the edited text as the current draft.
- Do not handle modified arrow keys or arrow-key events during IME composition.
- Existing Enter and Shift+Enter submission/newline behavior must remain unchanged.

## Acceptance criteria

Add focused black-box Playwright coverage proving that:

1. After sending two prompts to one role, successive `ArrowUp` presses recall them in newest-to-oldest order, and `ArrowDown` traverses back toward the newest prompt.
2. Moving past the newest entry restores the draft that existed before navigation.
3. Prompt histories remain isolated between role composers.
4. `ArrowUp` within a non-empty multiline prompt retains normal caret behavior unless the caret is at the absolute beginning.
5. Recalling and resubmitting a prompt sends the existing `prompt.send` envelope and clears the composer as it does today.

## Implementation plan

Keep this feature inside the dashboard's transient presentation state. Add a focused prompt-history composable beside
`useInteractionDrafts`; instantiate it once in `useDashboardSession`, where successful prompt submission is already
trimmed and converted into the existing protocol command. Keep DOM selection and keyboard-event eligibility in
`PromptComposer`. Route only history-navigation intents through `RolePanel` and `App`; do not add protocol messages,
backend state, or browser persistence.

### Slice 1: Recall and resend bounded per-role history

**Outcome:** From an empty composer, an operator can traverse and resend the current dashboard's recently submitted
prompts without seeing prompts submitted to another role.

1. Add per-role in-memory history and navigation state that records the exact trimmed prompt carried by every
   successful `prompt.send`, retains duplicates, ignores submissions that produce no command, and evicts the oldest
   entries beyond 50.
2. Preserve the existing submission boundary in `useDashboardSession`: send the unchanged `prompt.send` envelope,
   record only the value actually sent, clear the composer, and reset that role's navigation state.
3. Route older/newer navigation intents from `PromptComposer` through `RolePanel` and `App`. For an empty composer,
   handle only unmodified, non-composing `ArrowUp`; let repeated Up/Down presses traverse without wrapping, and move
   past the newest entry back to the empty draft and out of navigation mode. Do not consume arrows when no history
   navigation is active or available.
4. Add focused black-box coverage in a dedicated Playwright prompt-history spec for newest-to-oldest and reverse
   traversal, both history boundaries, isolation between role composers, trimmed-value recording, blank submission,
   the 50-entry limit, dashboard-reload reset, and recall/resubmit preserving the existing envelope and clear-on-send
   behavior.
5. Keep the existing exact-Enter submission and modified-Enter newline coverage passing while introducing arrow-key
   handling.

**Exit criteria:** Acceptance criteria 1, 3, and 5 pass through the rendered dashboard, history is bounded and
dashboard-local, and no C# or UI-protocol surface changes.

### Slice 2: Preserve drafts and native textarea editing

**Outcome:** An operator can enter history from an existing draft only at the absolute beginning, recover that draft,
and otherwise retain native multiline, editing, modifier, and IME behavior.

1. On the first eligible Up press from a non-empty composer, preserve that role's unsent draft. Moving Down past the
   newest submitted prompt restores the draft exactly and leaves navigation mode.
2. Start navigation from non-empty text only when the textarea selection is collapsed at offset zero. Once navigation
   is active, allow repeated unmodified Up/Down traversal; stop at either end without wrapping.
3. Treat user input applied to a recalled value as a new current draft and leave navigation mode, so the next eligible
   Up starts a fresh traversal while the edited text can still be restored.
4. Leave modified arrow events, composing keyboard events, and Up presses at any other non-empty selection/caret
   position to the textarea. Preserve the current Enter-to-send and Shift+Enter-to-insert-newline behavior.
5. Extend the focused Playwright coverage to prove draft restoration, editing a recalled prompt, collapsed-selection
   and absolute-start gating in multiline text, native behavior away from the start, modifier and IME safeguards, and
   unchanged Enter/Shift+Enter behavior.

**Exit criteria:** Acceptance criteria 2 and 4 pass, all keyboard rules in the issue are covered through browser-visible
behavior, and the complete focused prompt-history and dashboard-interaction Playwright suites plus the `squad-ui`
build pass.
