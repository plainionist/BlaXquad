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
