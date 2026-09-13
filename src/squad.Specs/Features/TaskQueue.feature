Feature: Single-task queue

  A task role accepts one durable handoff at a time in priority order.

  Background:

    Given `blaxquad/squad.json` configures:
      | role     |
      | reviewer |

  Scenario: An empty queue has no task

    When the "reviewer" role agent runs `squad ready-for-next` from its worktree

    Then the command succeeds
    And standard output contains "NO_TASK"

  Scenario: A role cannot be resolved from an unrelated nested directory

    Given a nested directory exists

    When the nested directory runs `squad ready-for-next`

    Then the command fails
    And standard error contains "Could not resolve the current role from its worktree."

  Scenario: Ambiguous worktree identity is rejected without legacy fallback

    Given a Git project with two roles sharing the current worktree

    When the ambiguous current worktree runs `squad ready-for-next`

    Then the command fails
    And standard error contains "Ambiguous current worktree"

  Scenario: An explicitly empty receive mode does not identify the role

    Given the "reviewer" role has an empty receive mode

    When the "reviewer" role agent runs `squad ready-for-next` from its worktree

    Then the command exits with code 1
    And standard error contains "Unknown role: reviewer"

  Scenario: An unsupported command-side receive mode is reported distinctly from an empty one

    Given the "reviewer" role has an unsupported receive mode "nightly"

    When the "reviewer" role agent runs `squad ready-for-next` from its worktree

    Then the command exits with code 2
    And standard error contains "INVALID_RECEIVE_MODE: nightly for role reviewer"

  Scenario: The highest-priority handoff is accepted first

    Given "reviewer" has these queued tasks:
      | from      | priority | task             |
      | architect | 50       | simplify-storage |
      | coder     | 10       | repair-delivery  |

    When the "reviewer" role agent runs `squad ready-for-next` from its worktree

    Then the command succeeds
    And task "repair-delivery" is in process
    And task "simplify-storage" remains queued
    And standard output contains "TASK_NAME: repair-delivery"

  Scenario: Completing a task immediately accepts the next task

    Given "reviewer" is processing task "repair-delivery" from "coder"
    And "reviewer" has this queued task:
      | from      | priority | task             |
      | architect | 50       | simplify-storage |

    When the "reviewer" role agent runs `squad done-with-current` from its worktree

    Then the command succeeds
    And task "repair-delivery" is completed
    And task "simplify-storage" is in process
    And standard output contains "COMPLETED:"
    And standard output contains "TASK_NAME: simplify-storage"

  Scenario: Multiple current tasks are rejected as ambiguous

    Given "reviewer" is processing task "first-task" from "coder"
    And "reviewer" is also processing task "second-task" from "architect"

    When the "reviewer" role agent runs `squad ready-for-next` from its worktree

    Then the command exits with code 2
    And standard error contains "multiple tasks are already in process"

  Scenario: A role resumes its current task after restart

    Given "reviewer" is processing task "repair-delivery" from "coder"

    When the "reviewer" role agent runs `squad ready-for-next` from its worktree

    Then the command succeeds
    And task "repair-delivery" is in process
    And standard output contains "TASK_NAME: repair-delivery"

  Scenario: An archive collision cannot lose the current task

    Given "reviewer" is processing task "repair-delivery" from "coder"
    And the completion archive already contains that task

    When the "reviewer" role agent runs `squad done-with-current` from its worktree

    Then the command exits with code 2
    And standard error contains "completed file already exists"
    And task "repair-delivery" is in process
