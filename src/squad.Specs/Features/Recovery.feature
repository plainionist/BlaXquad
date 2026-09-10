Feature: Recovering durable work
  Restarting a tool resumes durable state without duplicating or losing work.

  Scenario: Retrying an already persisted delivery creates no duplicate
    Given `blaxquad/squad.json` configures:
      | role     |
      | coder    |
      | reviewer |
    And "coder" prepares a note to "reviewer" with priority "50" and message "Ready for review."
    And the "coder" role agent runs `squad handoff` from its worktree
    And "reviewer" already has the recipient copy
    When the operator launches Headquarters, continuing from durable state
    Then the sender handoff is archived as sent
    And "reviewer" has one new handoff
    And "reviewer"'s recipient copy is unchanged

  Scenario: Existing inbox work survives a headquarters restart unchanged
    Given `blaxquad/squad.json` configures:
      | role     |
      | reviewer |
    And "reviewer" has existing new inbox work "recovery-check-new" from "coder"
    And "reviewer" has existing in-process inbox work "recovery-check-in-process" from "coder"
    And the existing inbox work for "reviewer" is recorded
    When the operator launches Headquarters, continuing from durable state
    Then the "reviewer" agent observes a recovery wake-up message
    And the existing inbox work for "reviewer" remains unchanged
    When the operator restarts Headquarters, continuing from durable state
    Then the "reviewer" agent observes a recovery wake-up message
    And the existing inbox work for "reviewer" remains unchanged

  Scenario Outline: Unavailable recipient work survives until a later session is available
    Given `blaxquad/squad.json` configures:
      | role     |
      | coder    |
      | reviewer |
    When the operator launches Headquarters, continuing from durable state
    And "reviewer"'s session <lifecycle>
    And "coder" prepares a note to "reviewer" with priority "50" and message "Ready for review."
    And the "coder" role agent runs `squad handoff` from its worktree
    Then the sender handoff is archived as sent
    And "reviewer" has one new handoff
    When the operator restarts Headquarters, continuing from durable state
    Then the "reviewer" agent observes a recovery wake-up message

    Examples:
      | lifecycle |
      | stops     |
      | fails     |
