Feature: Abort sequencing and terminal role failure

  Aborting a role through the real "role.abort" UI command reaches only the addressed role's fake session, cancels
  that role's outstanding prompt operation, and leaves the role unable to accept a following prompt until the abort
  itself has finished - a prompt cannot overtake an in-flight abort, and stale events published during a cancelled
  turn never reach the published transcript. Repeated idle aborts and a retry after a failed abort remain
  observable purely through their user-visible command results and the role's subsequent availability. A terminal
  session failure affects only its own role: it publishes the role's error status, removes its pending
  interactions, rejects later commands addressed to it, suppresses its late provider events, and never prevents a
  healthy role from continuing to handle prompts - observed only through the real UI protocol and the fake-agent
  control pipe, never through SquadViewModel, its role or interaction collections, the operation coordinator, or
  "Recording*" objects.

  Background:
    Given a backend scenario configured with roles "coder,reviewer"
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    And the backend scenario observes a session started for role "reviewer" across the control pipe

  Scenario: Abort is routed only to the addressed role and leaves it not ready for a prompt
    When the "coder" agent emits idle
    And the backend scenario requests an abort for role "coder"
    Then the "coder" agent observes an abort
    And the "reviewer" agent has not observed an abort
    And role "coder" is not ready for a prompt

  Scenario: Aborting a role with an outstanding prompt cancels that prompt's operation
    When the "coder" agent emits idle
    And the backend scenario sends the prompt "Investigate the bug" to role "coder"
    Then the "coder" agent observes the prompt "Investigate the bug"
    When the backend scenario requests an abort for role "coder"
    Then the "coder" agent observes an abort
    When the backend scenario sends the prompt "next" to role "coder"
    Then the "coder" agent observes the prompt "next"

  Scenario: A following prompt waits for an in-flight abort to finish before it is delivered
    When the "coder" agent emits idle
    And the backend scenario arms role "coder" to hold its next abort pending
    And the backend scenario sends the prompt "first" to role "coder"
    Then the "coder" agent observes the prompt "first"
    When the backend scenario requests an abort for role "coder"
    Then the "coder" agent observes an abort
    When the backend scenario sends the prompt "second" to role "coder"
    Then the "coder" agent has not received the prompt "second" within 2 seconds
    When the backend scenario completes the pending abort for role "coder"
    Then the "coder" agent observes the prompt "second"

  Scenario: Events published during a cancelled turn are ignored
    When the "coder" agent emits idle
    And the backend scenario requests an abort for role "coder"
    Then the "coder" agent observes an abort
    When the "coder" agent emits the reasoning "stale response"
    Then the backend scenario does not observe the transcript for role "coder" containing "stale response" within 2 seconds

  Scenario: Repeated idle aborts are safe and each is independently observable
    When the backend scenario requests an abort for role "coder"
    Then the "coder" agent observes an abort
    When the backend scenario requests an abort for role "coder"
    Then the "coder" agent observes 2 aborts

  Scenario: A failed abort can be retried and a following prompt then succeeds
    When the backend scenario arms role "coder" to fail its next abort with message "abort failed"
    And the backend scenario requests an abort for role "coder"
    Then the backend scenario observes a protocol error mentioning "abort failed"
    When the backend scenario requests an abort for role "coder"
    Then the "coder" agent observes 2 aborts
    When the backend scenario sends the prompt "next" to role "coder"
    Then the "coder" agent observes the prompt "next"

  Scenario: A terminal session failure affects only its role and rejects later commands
    When the "coder" agent requests permission "permission-1" with description "Deploy to prod?"
    Then the backend scenario observes a pending permission "permission-1" for role "coder" with description "Deploy to prod?"
    When the "coder" agent fails its session with message "event channel overloaded"
    Then the backend scenario observes role "coder" at status "error"
    And the backend scenario observes no pending permission "permission-1" for role "coder"
    When the "coder" agent emits the reasoning "should not resurrect the role"
    Then the backend scenario does not observe the transcript for role "coder" containing "should not resurrect the role" within 2 seconds
    When the backend scenario sends the prompt "still available" to role "reviewer"
    Then the "reviewer" agent observes the prompt "still available"
    When the "reviewer" agent replies with "reviewer done"
    Then the backend scenario observes the transcript for role "reviewer" containing "reviewer done"
    When the backend scenario sends the prompt "too late" to role "coder"
    Then the backend scenario observes a protocol error mentioning "unavailable"
