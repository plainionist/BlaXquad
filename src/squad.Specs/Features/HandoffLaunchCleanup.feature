Feature: Handoff queues are launch-scoped, not restart-safe

  Every Headquarters launch - fresh or continued - discards each configured worktree's complete handoff-state
  directory before any role session starts or any delivery poll runs. Handoffs are file-backed state for the
  current Headquarters run only; "--continue" preserves Git worktree content, never queued mail.

  Scenario: A normal launch discards a stale handoff queue without delivering or waking any role

    Given `blaxquad/squad.json` configures:
      | role     |
      | coder    |
      | reviewer |
    And role "coder" has a durable file ".blaxquad/handoffs/outbox/50_stale_from_coder_to_reviewer.handoff.json" containing "stale outbox artifact"
    And role "coder" has a durable file ".blaxquad/handoffs/sent/50_stale_sent.handoff.json" containing "stale sent artifact"
    And role "coder" has a durable file ".blaxquad/handoffs/failed/50_stale_failed.handoff.json" containing "stale failed artifact"
    And role "reviewer" has a durable file ".blaxquad/handoffs/inbox/new/50_stale_new.handoff.json" containing "stale new artifact"
    And role "reviewer" has a durable file ".blaxquad/handoffs/inbox/in_process/50_stale_in_process.handoff.json" containing "stale in-process artifact"
    And role "reviewer" has a durable file ".blaxquad/handoffs/inbox/completed/50_stale_completed.handoff.json" containing "stale completed artifact"
    And role "reviewer" has a durable file ".blaxquad/handoffs/inbox/in_process/batch_20260822T120000Z_000001/50_stale_batch_item.handoff.json" containing "stale batch artifact"
    And role "coder" has a durable file ".blaxquad/handoffs/outbox/50_legacy_from_coder_to_reviewer.handoff" containing "legacy queue artifact"

    When the operator launches Headquarters

    Then Headquarters starts an agent session for role "coder"
    And Headquarters starts an agent session for role "reviewer"
    And "reviewer" has no new handoff
    And the "reviewer" agent has not observed the handoff wake-up message
    And role "coder"'s durable file ".blaxquad/handoffs/outbox/50_stale_from_coder_to_reviewer.handoff.json" no longer exists
    And role "coder"'s durable file ".blaxquad/handoffs/sent/50_stale_sent.handoff.json" no longer exists
    And role "coder"'s durable file ".blaxquad/handoffs/failed/50_stale_failed.handoff.json" no longer exists
    And role "reviewer"'s durable file ".blaxquad/handoffs/inbox/new/50_stale_new.handoff.json" no longer exists
    And role "reviewer"'s durable file ".blaxquad/handoffs/inbox/in_process/50_stale_in_process.handoff.json" no longer exists
    And role "reviewer"'s durable file ".blaxquad/handoffs/inbox/completed/50_stale_completed.handoff.json" no longer exists
    And role "reviewer"'s durable file ".blaxquad/handoffs/inbox/in_process/batch_20260822T120000Z_000001/50_stale_batch_item.handoff.json" no longer exists
    And role "coder"'s durable file ".blaxquad/handoffs/outbox/50_legacy_from_coder_to_reviewer.handoff" no longer exists

  Scenario: A continued launch also discards a stale handoff queue while preserving unrelated worktree content

    Given `blaxquad/squad.json` configures:
      | role     |
      | coder    |
      | reviewer |
    And role "coder" has a durable file "notes.md" containing "Keep this note."
    And role "coder" has a durable file ".blaxquad/handoffs/outbox/50_stale_from_coder_to_reviewer.handoff.json" containing "stale outbox artifact"
    And role "reviewer" has a durable file ".blaxquad/handoffs/inbox/new/50_stale_new.handoff.json" containing "stale new artifact"
    And role "reviewer" has a durable file ".blaxquad/handoffs/inbox/in_process/batch_20260822T120000Z_000001/50_stale_batch_item.handoff.json" containing "stale batch artifact"

    When the operator launches Headquarters, continuing from durable state

    Then Headquarters starts an agent session for role "coder"
    And Headquarters starts an agent session for role "reviewer"
    And "reviewer" has no new handoff
    And the "reviewer" agent has not observed the handoff wake-up message
    And role "coder"'s durable file "notes.md" still contains "Keep this note."
    And role "coder"'s durable file ".blaxquad/handoffs/outbox/50_stale_from_coder_to_reviewer.handoff.json" no longer exists
    And role "reviewer"'s durable file ".blaxquad/handoffs/inbox/new/50_stale_new.handoff.json" no longer exists
    And role "reviewer"'s durable file ".blaxquad/handoffs/inbox/in_process/batch_20260822T120000Z_000001/50_stale_batch_item.handoff.json" no longer exists

  Scenario: A handoff created after a fresh launch still delivers, notifies, and completes normally

    Given `blaxquad/squad.json` configures:
      | role     |
      | coder    |
      | reviewer |
    And role "reviewer" has a durable file ".blaxquad/handoffs/inbox/new/50_stale_new.handoff.json" containing "stale new artifact"

    When the operator launches Headquarters

    Then Headquarters starts an agent session for role "coder"
    And Headquarters starts an agent session for role "reviewer"
    And "reviewer" has no new handoff

    Given "coder" prepares a note with priority "50" and message "Ready for review." to:
      | role     |
      | reviewer |

    When the "coder" role agent runs `squad handoff` from its worktree

    Then the sender handoff is archived as sent
    And "reviewer" has one new handoff
    And the "reviewer" agent observes the handoff wake-up message
