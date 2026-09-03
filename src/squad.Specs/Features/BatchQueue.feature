Feature: Batch queue
  A batch role accepts all currently queued handoffs at the highest priority.

  Background:
    Given a Git project with batch role "cleaner"

  Scenario: An empty queue has no batch
    When "cleaner" checks for work
    Then the command succeeds
    And standard output contains "NO_TASK"

  Scenario: Equal-priority handoffs form one batch
    Given "cleaner" has these queued tasks:
      | from      | priority | task              |
      | coder     | 10       | repair-delivery   |
      | reviewer  | 10       | verify-delivery   |
      | architect | 50       | simplify-storage  |
    When "cleaner" checks for work
    Then the command succeeds
    And task "repair-delivery" is in process
    And task "verify-delivery" is in process
    And task "simplify-storage" remains queued
    And standard output contains "COUNT: 2"
    And standard output contains "PRIORITY: 10"

  Scenario: Completing a batch immediately accepts the next batch
    Given "cleaner" is processing this batch:
      | from     | priority | task            |
      | coder    | 10       | repair-delivery |
      | reviewer | 10       | verify-delivery |
    And "cleaner" has this queued task:
      | from      | priority | task             |
      | architect | 50       | simplify-storage |
    When "cleaner" completes the current work
    Then the command succeeds
    And task "repair-delivery" is completed
    And task "verify-delivery" is completed
    And task "simplify-storage" is in process
    And standard output contains "COMPLETED_BATCH:"
    And standard output contains "TASK_NAME: simplify-storage"

  Scenario: A single current task is rejected for a batch role
    Given "cleaner" is processing task "repair-delivery" from "coder"
    When "cleaner" checks for work
    Then the command exits with code 2
    And standard error contains "TASK_IN_PROCESS_IS_SINGLE"
