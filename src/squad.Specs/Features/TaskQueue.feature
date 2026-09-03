Feature: Single-task queue
  A task role accepts one durable handoff at a time in priority order.

  Background:
    Given a Git project with task role "reviewer"

  Scenario: An empty queue has no task
    When "reviewer" checks for work
    Then the command succeeds
    And standard output contains "NO_TASK"

  Scenario: A role cannot be resolved from an unrelated nested directory
    Given a nested directory exists
    When the nested directory checks for work
    Then the command succeeds
    And standard output contains "NO_TASK"

  Scenario: Ambiguous worktree identity is rejected without legacy fallback
    Given a Git project with two roles sharing the current worktree
    When the ambiguous current worktree checks for work
    Then the command fails
    And standard error contains "Ambiguous current worktree"

  Scenario: An explicitly empty receive mode does not identify the role
    Given a Git project with role "reviewer" and an empty receive mode
    When "reviewer" checks for work
    Then the command exits with code 1
    And standard error contains "Unknown role: reviewer"

  Scenario: The highest-priority handoff is accepted first
    Given "reviewer" has these queued tasks:
      | from      | priority | task             |
      | architect | 50       | simplify-storage |
      | coder     | 10       | repair-delivery  |
    When "reviewer" checks for work
    Then the command succeeds
    And task "repair-delivery" is in process
    And task "simplify-storage" remains queued
    And standard output contains "TASK_NAME: repair-delivery"

  Scenario: Completing a task immediately accepts the next task
    Given "reviewer" is processing task "repair-delivery" from "coder"
    And "reviewer" has this queued task:
      | from      | priority | task             |
      | architect | 50       | simplify-storage |
    When "reviewer" completes the current work
    Then the command succeeds
    And task "repair-delivery" is completed
    And task "simplify-storage" is in process
    And standard output contains "COMPLETED:"
    And standard output contains "TASK_NAME: simplify-storage"

  Scenario: Multiple current tasks are rejected as ambiguous
    Given "reviewer" is processing task "first-task" from "coder"
    And "reviewer" is also processing task "second-task" from "architect"
    When "reviewer" checks for work
    Then the command exits with code 2
    And standard error contains "multiple tasks are already in process"
