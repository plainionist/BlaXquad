Feature: Recovering durable work
  Restarting a tool resumes durable state without duplicating or losing work.

  Scenario: Retrying an already persisted delivery creates no duplicate
    Given delivery roles "coder,reviewer"
    And "coder" has an outbound note to "reviewer"
    And "reviewer" already has the recipient copy
    When the squad host processes the handoff outbox
    Then the sender handoff is archived as sent
    And "reviewer" has one new handoff
    And "reviewer"'s recipient copy is unchanged

  Scenario: Existing inbox work survives a headquarters restart unchanged
    Given delivery roles "reviewer"
    And "reviewer" has existing new inbox work "recovery-check-new" from "coder"
    And "reviewer" has existing in-process inbox work "recovery-check-in-process" from "coder"
    And the existing inbox work for "reviewer" is recorded
    When the squad host starts
    Then the "reviewer" agent observes a recovery wake-up message
    And the existing inbox work for "reviewer" remains unchanged
    When the squad host restarts with a replacement session
    Then the "reviewer" agent observes a recovery wake-up message
    And the existing inbox work for "reviewer" remains unchanged

  Scenario Outline: Unavailable recipient work survives until a later session is available
    Given delivery roles "coder,reviewer"
    When the squad host starts
    And "reviewer"'s session <lifecycle>
    And "coder" has an outbound note to "reviewer"
    Then the sender handoff is archived as sent
    And "reviewer" has one new handoff
    When the squad host restarts with a replacement session
    Then the "reviewer" agent observes a recovery wake-up message

    Examples:
      | lifecycle |
      | stops     |
      | fails     |
