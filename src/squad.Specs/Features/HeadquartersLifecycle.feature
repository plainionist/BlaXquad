Feature: Healthy headquarters lifecycle

  An operator launches a real multi-role squad-hq process, waits for every configured agent session to reach
  readiness, shuts it down cleanly through the public `squad-hq shutdown` Headquarters-control command, and can
  relaunch it against the same project afterward. Headquarters preserves durable workspace state it does not own
  and releases every externally observable resource on shutdown - proven only through the real process, the real
  UI protocol, and the real `squad-hq wait-for-agent` and `squad-hq shutdown` Headquarters-control commands, never
  through SquadApplication, a Headquarters lease object, or a lifecycle trace.

  Scenario: A healthy multi-role headquarters process reaches full readiness and shuts down through Headquarters control
    Given `blaxquad/squad.json` configures:
      | role     |
      | coder    |
      | reviewer |
    And role "coder" has a durable file "notes.md" containing "Keep this note."
    When the operator launches Headquarters
    Then Headquarters starts an agent session for role "coder"
    And Headquarters starts an agent session for role "reviewer"
    And the "coder" agent observes a harness message
    And the "reviewer" agent observes a harness message
    When the "coder" agent emits idle
    And the "reviewer" agent emits idle
    Then the operator confirms role "coder" is ready with `squad-hq wait-for-agent`
    And the operator confirms role "reviewer" is ready with `squad-hq wait-for-agent`
    When the operator shuts down Headquarters
    Then Headquarters exits with code 0
    And Headquarters disposes the agent session for role "coder"
    And Headquarters disposes the agent session for role "reviewer"
    And the operator finds Headquarters unavailable for role "coder"
    And role "coder"'s durable file "notes.md" still contains "Keep this note."
    When the operator launches a new Headquarters against the same project
    Then the new Headquarters process reports ready
