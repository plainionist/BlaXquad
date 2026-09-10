Feature: Surfacing terminal provider failures after readiness

  A real squad-hq process distinguishes a per-session provider failure, which affects only its own role and never
  stops the host, from a fatal, backend-wide provider failure, which reports a clear diagnostic on standard error
  (never a raw ".NET Unhandled exception" dump), exits with a non-zero code, disposes every session already
  started, never leaves a live host behind, preserves durable workspace state it does not own, remains the
  reported outcome even when a normal shutdown is requested around the same time, and permits a fresh, healthy
  launch against the same project afterward - proven only through the real process, the real "squad-hq shutdown"
  and "squad-hq wait-for-agent" host-control commands, and the fake-provider control pipe, never through
  SquadApplication directly.

  Scenario: A per-session failure after readiness marks only that role's terminal state and never stops the host
    Given `blaxquad/squad.json` configures:
      | role     |
      | coder    |
      | reviewer |
    When the operator launches Headquarters
    Then Headquarters starts an agent session for role "coder"
    And Headquarters starts an agent session for role "reviewer"
    When the "coder" agent fails its session with message "SDK unavailable"
    Then the dashboard shows role "coder" at status "error"
    When the user sends "still available" to role "reviewer"
    Then the "reviewer" agent observes the prompt "still available"
    When the operator shuts down Headquarters
    Then Headquarters exits with code 0
    And Headquarters disposes the agent session for role "coder"
    And Headquarters disposes the agent session for role "reviewer"

  Scenario: A backend-wide terminal failure after readiness stops the host with the original diagnostic
    Given `blaxquad/squad.json` configures:
      | role  |
      | coder |
    And role "coder" has a durable file "notes.md" containing "Keep this note."
    When the operator launches Headquarters
    Then Headquarters starts an agent session for role "coder"
    When the agent provider fails its backend with message "shared SDK force-stop failed"
    And Headquarters' process exits on its own
    Then Headquarters exits with a non-zero code
    And Headquarters' standard error contains "shared SDK force-stop failed"
    And Headquarters' standard error does not contain "Unhandled exception"
    And Headquarters' standard error does not contain "Provider startup failed"
    And Headquarters disposes the agent session for role "coder"
    And the operator finds Headquarters unavailable for role "coder"
    And role "coder"'s durable file "notes.md" still contains "Keep this note."
    When the operator launches a new Headquarters against the same project
    Then the new Headquarters process reports ready

  Scenario: A backend-wide terminal failure remains the reported outcome even when shutdown is also requested
    Given `blaxquad/squad.json` configures:
      | role  |
      | coder |
    And role "coder" has a durable file "notes.md" containing "Keep this note."
    When the operator launches Headquarters
    Then Headquarters starts an agent session for role "coder"
    When the agent provider fails its backend with message "shared SDK force-stop failed"
    And the operator shuts down Headquarters
    Then Headquarters exits with a non-zero code
    And Headquarters' standard error contains "shared SDK force-stop failed"
    And Headquarters' standard error does not contain "Unhandled exception"
    And Headquarters' standard error does not contain "Provider startup failed"
    And Headquarters disposes the agent session for role "coder"
    And the operator finds Headquarters unavailable for role "coder"
    And role "coder"'s durable file "notes.md" still contains "Keep this note."
    When the operator launches a new Headquarters against the same project
    Then the new Headquarters process reports ready
