Feature: Delivering handoffs
  Headquarters persists each recipient's handoff before sending a wake-up.

  Background:
    Given `blaxquad/squad.json` configures:
      | role     |
      | coder    |
      | reviewer |
    When the operator launches Headquarters
    Then Headquarters starts an agent session for role "coder"
    And Headquarters starts an agent session for role "reviewer"

  Scenario: Deliver a handoff and notify its recipient
    Given "coder" prepares a note with priority "50" and message "Ready for review." to:
      | role     |
      | reviewer |
    When the "coder" role agent runs `squad handoff` from its worktree
    Then the sender handoff is archived as sent
    And "reviewer" has one new handoff
    And the new handoff for "reviewer" has recipient header "reviewer"
    And the "reviewer" agent observes the handoff wake-up message

  Scenario: Reject an invalid fan-out before delivering any copy
    When "coder" durably queues an invalid note to:
      | role     |
      | reviewer |
      | missing  |
    Then the sender handoff is archived as failed
    And "reviewer" has no new handoff
    And the "reviewer" agent has not observed the handoff wake-up message

  Scenario: Notification failure does not lose a delivered handoff
    Given the "reviewer" agent will reject its next harness send
    And "coder" prepares a note with priority "50" and message "Ready for review." to:
      | role     |
      | reviewer |
    When the "coder" role agent runs `squad handoff` from its worktree
    Then the sender handoff is archived as sent
    And "reviewer" has one new handoff
    And the "reviewer" agent's rejected harness send proves no wake-up was delivered
    And Headquarters remains available

  Scenario: A busy recipient's current prompt is not interrupted by a delivery wake-up
    Given "reviewer" is busy with a prompt
    And "coder" prepares a note with priority "50" and message "Ready for review." to:
      | role     |
      | reviewer |
    When the "coder" role agent runs `squad handoff` from its worktree
    Then the sender handoff is archived as sent
    And "reviewer" has one new handoff
    And the "reviewer" agent has not observed the handoff wake-up message
    When "reviewer" finishes its prompt
    Then the "reviewer" agent observes the handoff wake-up message
