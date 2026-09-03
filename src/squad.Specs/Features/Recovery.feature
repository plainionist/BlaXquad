Feature: Recovering durable work
  Restarting a tool resumes durable state without duplicating or losing work.

  Scenario: A role resumes its current task after restart
    Given a Git project with task role "reviewer"
    And "reviewer" is processing task "repair-delivery" from "coder"
    When "reviewer" checks for work
    Then the command succeeds
    And task "repair-delivery" is in process
    And standard output contains "TASK_NAME: repair-delivery"

  Scenario: Retrying an already persisted delivery creates no duplicate
    Given delivery roles "coder,reviewer"
    And "coder" has an outbound note to "reviewer"
    And "reviewer" already has the recipient copy
    When the squad host processes the handoff outbox
    Then the sender handoff is archived as sent
    And "reviewer" has one new handoff

  Scenario: An archive collision cannot lose the current task
    Given a Git project with task role "reviewer"
    And "reviewer" is processing task "repair-delivery" from "coder"
    And the completion archive already contains that task
    When "reviewer" completes the current work
    Then the command exits with code 2
    And standard error contains "completed file already exists"
    And task "repair-delivery" is in process
