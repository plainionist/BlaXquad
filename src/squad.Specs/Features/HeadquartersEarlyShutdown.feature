Feature: Stopping safely before and during startup

  A real squad-hq process that receives a host-control shutdown request at any point before it reaches readiness -
  whether requested as early as the process can possibly be reached, or while provider startup is genuinely
  paused part-way through registering role sessions - never publishes readiness, never delivers a new command to
  a provider session, disposes every session already registered, terminates cleanly, preserves durable workspace
  state it does not own, releases every externally observable resource, and permits a fresh, healthy launch
  against the same project afterward - proven only through the real process, the real "squad-hq shutdown" and
  "squad-hq wait-for-agent" host-control commands, and the fake-provider control pipe, never through
  SquadApplication or a lifecycle trace.

  Scenario: Shutdown requested as early as possible prevents startup from ever completing
    Given `blaxquad/squad.json` configures:
      | role  |
      | coder |
    And role "coder" has a durable file "notes.md" containing "Keep this note."
    When the operator launches Headquarters without completing the ready handshake
    And the operator begins waiting for role "coder" to become ready with `squad-hq wait-for-agent`
    And the operator requests shutdown as soon as it is reachable
    And the user sends "too late" to role "coder"
    And Headquarters' pending shutdown completes
    Then Headquarters exits with code 0
    And role "coder" was never reported ready
    And Headquarters never starts an agent session for role "coder"
    And the "coder" agent has received no prompt
    And the operator finds Headquarters unavailable for role "coder"
    And role "coder"'s durable file "notes.md" still contains "Keep this note."
    When the operator launches a new Headquarters against the same project
    Then the new Headquarters process reports ready

  Scenario: Shutdown requested while provider startup is paused disposes the already-started session
    Given `blaxquad/squad.json` configures:
      | role     |
      | coder    |
      | reviewer |
    And Headquarters' startup pauses after 1 session has started
    And role "coder" has a durable file "notes.md" containing "Keep this note."
    When the operator launches Headquarters
    Then Headquarters starts an agent session for role "coder"
    And Headquarters never starts an agent session for role "reviewer"
    When the operator begins waiting for role "coder" to become ready with `squad-hq wait-for-agent`
    And the operator begins shutting down Headquarters without waiting for it to exit
    And the user sends "too late" to role "reviewer"
    And Headquarters' process exits on its own
    Then Headquarters exits with code 0
    And role "coder" was never reported ready
    And Headquarters disposes the agent session for role "coder"
    And Headquarters never starts an agent session for role "reviewer"
    And the "reviewer" agent has received no prompt
    And the operator finds Headquarters unavailable for role "coder"
    And role "coder"'s durable file "notes.md" still contains "Keep this note."
    When the operator launches a new Headquarters against the same project
    Then the new Headquarters process reports ready
