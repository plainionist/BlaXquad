Feature: Creating outbound handoffs
  Agents create small validated drafts and the squad publishes durable handoffs.

  Background:
    Given `blaxquad/squad.json` configures:
      | role      |
      | coder     |
      | reviewer  |
      | architect |

  Scenario: Queue a Git handoff for a committed change
    Given "coder" has a committed change
    And "coder" prepares a Git handoff with priority "20" and task "implement-search" to:
      | role     |
      | reviewer |
    When the "coder" role agent runs `squad handoff` from its worktree
    Then the command succeeds
    And the draft is removed
    And one handoff is queued
    And the queued handoff was sent by "coder" to:
      | role     |
      | reviewer |
    And the queued handoff has priority "20"
    And the queued handoff is a Git handoff for task "implement-search"
    And the queued handoff instructs merging the committed change

  Scenario: Queue a note for several recipients
    Given "coder" prepares a note with priority "70" and message "Please inspect the delivery result." to:
      | role      |
      | reviewer  |
      | architect |
    When the "coder" role agent runs `squad handoff` from its worktree
    Then the command succeeds
    And one handoff is queued
    And the queued handoff was sent by "coder" to:
      | role      |
      | reviewer  |
      | architect |
    And the queued handoff is a note with message "Please inspect the delivery result."

  Scenario: Reject all repairable errors in an invalid draft
    Given "coder" prepares this handoff draft:
      """
      type: note
      to: reviewer,missing
      priority: urgent
      completed_at: yesterday
      message: Please inspect the delivery result.
      """
    When the "coder" role agent runs `squad handoff` from its worktree
    Then the command exits with code 2
    And standard error contains "priority"
    And standard error contains "reserved"
    And standard error contains "Unknown recipient role 'missing'"
    And the draft remains
    And no handoff is queued
