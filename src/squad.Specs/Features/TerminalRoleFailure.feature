Feature: Terminal role failure

  A terminal session failure affects only its own role: it publishes the role's error status, removes its pending
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
