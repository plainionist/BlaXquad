Feature: Abort ordering

  Aborting a role through the real "role.abort" UI command reaches only the addressed role, cancels that role's
  outstanding prompt operation, and leaves the role unable to accept a following prompt until the abort itself has
  finished - a prompt cannot overtake an in-flight abort, and stale events published during a cancelled turn never
  reach the published transcript. Repeated idle aborts and a retry after a failed abort remain observable purely
  through their user-visible command results and the role's subsequent availability.

  Background:
    Given `blaxquad/squad.json` configures:
      | role     |
      | coder    |
      | reviewer |
    When the operator launches Headquarters
    Then Headquarters starts an agent session for role "coder"
    And Headquarters starts an agent session for role "reviewer"

  Scenario: Abort is routed only to the addressed role and leaves it not ready for a prompt
    When the "coder" agent emits idle
    And the user aborts role "coder"
    Then the "coder" agent observes an abort
    And the "reviewer" agent has not observed an abort
    And role "coder" is not ready for a prompt

  Scenario: Aborting a role with an outstanding prompt cancels that prompt's operation
    When the "coder" agent emits idle
    And the user sends "Investigate the bug" to role "coder"
    Then the "coder" agent observes the prompt "Investigate the bug"
    When the user aborts role "coder"
    Then the "coder" agent observes an abort
    When the user sends "next" to role "coder"
    Then the "coder" agent observes the prompt "next"

  Scenario: A following prompt waits for an in-flight abort to finish before it is delivered
    When the "coder" agent emits idle
    And the "coder" agent holds its next abort pending
    And the user sends "first" to role "coder"
    Then the "coder" agent observes the prompt "first"
    When the user aborts role "coder"
    Then the "coder" agent observes an abort
    When the user sends "second" to role "coder"
    Then the "coder" agent has not received the prompt "second" within 2 seconds
    When the "coder" agent releases its pending abort
    Then the "coder" agent observes the prompt "second"

  Scenario: Events published during a cancelled turn are ignored
    When the "coder" agent emits idle
    And the user aborts role "coder"
    Then the "coder" agent observes an abort
    When the "coder" agent emits the reasoning "stale response"
    Then the transcript for role "coder" does not contain "stale response" within 2 seconds

  Scenario: Repeated idle aborts are safe and each is independently observable
    When the user aborts role "coder"
    Then the "coder" agent observes an abort
    When the user aborts role "coder"
    Then the "coder" agent observes 2 aborts

  Scenario: A failed abort can be retried and a following prompt then succeeds
    When the "coder" agent fails its next abort with message "abort failed"
    And the user aborts role "coder"
    Then the user observes a protocol error mentioning "abort failed"
    When the user aborts role "coder"
    Then the "coder" agent observes 2 aborts
    When the user sends "next" to role "coder"
    Then the "coder" agent observes the prompt "next"
