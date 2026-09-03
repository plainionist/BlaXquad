Feature: Creating outbound handoffs
  Agents create small validated drafts and the squad publishes durable handoffs.

  Background:
    Given a Git project with roles "coder,reviewer,architect"

  Scenario: Queue a Git handoff for a committed change
    Given "coder" has a committed change
    And "coder" prepares a Git handoff to "reviewer" with priority "20" and task "implement-search"
    When "coder" queues the handoff
    Then the command succeeds
    And the draft is removed
    And one handoff is queued
    And the queued handoff has header "from" with value "coder"
    And the queued handoff has header "to" with value "reviewer"
    And the queued handoff has header "priority" with value "20"
    And the queued handoff has header "task" with value "implement-search"
    And the queued handoff payload starts with "merge_and_process coder "

  Scenario: Queue a note for several recipients
    Given "coder" prepares a note to "reviewer,architect" with priority "70" and message "Please inspect the delivery result."
    When "coder" queues the handoff
    Then the command succeeds
    And one handoff is queued
    And the queued handoff has header "to" with value "reviewer,architect"
    And the queued handoff has header "type" with value "note"
    And the queued handoff payload is "Please inspect the delivery result."

  Scenario: Reject all repairable errors in an invalid draft
    Given "coder" prepares this handoff draft:
      """
      type: note
      to: reviewer,missing
      priority: urgent
      completed_at: yesterday
      message: Please inspect the delivery result.
      """
    When "coder" queues the handoff
    Then the command exits with code 2
    And standard error contains "priority"
    And standard error contains "reserved"
    And standard error contains "Unknown recipient role 'missing'"
    And the draft remains
    And no handoff is queued
