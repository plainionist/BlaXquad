Feature: Delivering handoffs
  The squad host persists each recipient's handoff before sending a wake-up.

  Background:
    Given a running squad host for roles "coder,reviewer"

  Scenario: Deliver a handoff and notify its recipient
    Given "coder" prepares a note to "reviewer" with priority "50" and message "Ready for review."
    When "coder" queues the handoff
    Then the sender handoff is archived as sent
    And "reviewer" has one new handoff
    And the new handoff for "reviewer" has recipient header "reviewer"
    And the "reviewer" agent observes the handoff wake-up message

  Scenario: Reject an invalid fan-out before delivering any copy
    When "coder" durably queues an invalid note to "reviewer,missing"
    Then the sender handoff is archived as failed
    And "reviewer" has no new handoff
    And the "reviewer" agent has not observed the handoff wake-up message

  Scenario: Notification failure does not lose a delivered handoff
    Given the "reviewer" agent will reject its next harness send
    And "coder" prepares a note to "reviewer" with priority "50" and message "Ready for review."
    When "coder" queues the handoff
    Then the sender handoff is archived as sent
    And "reviewer" has one new handoff
    And the "reviewer" agent's rejected harness send proves no wake-up was delivered
    And the squad host remains available
