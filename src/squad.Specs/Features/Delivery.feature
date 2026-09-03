Feature: Delivering handoffs
  The squad host persists each recipient's handoff before sending a wake-up.

  Background:
    Given delivery roles "coder,reviewer,architect"

  Scenario: Deliver a handoff and notify its recipient
    Given "coder" has an outbound note to "reviewer"
    When the squad host processes the handoff outbox
    Then the sender handoff is archived as sent
    And "reviewer" has one new handoff
    And the new handoff for "reviewer" has recipient header "reviewer"
    And a wake-up was recorded for "reviewer"
    And the wake-up names the installed ready command

  Scenario: Reject an invalid fan-out before delivering any copy
    Given "coder" has an outbound note to "reviewer,missing"
    When the squad host processes the handoff outbox
    Then the sender handoff is archived as failed
    And "reviewer" has no new handoff
    And no wake-up was recorded

  Scenario: Notification failure does not lose a delivered handoff
    Given the recording notifier will fail
    And "coder" has an outbound note to "reviewer"
    When the squad host processes the handoff outbox
    Then the sender handoff is archived as sent
    And "reviewer" has one new handoff
    And a wake-up was recorded for "reviewer"
    And the delivery log contains "notify-failed reviewer"
