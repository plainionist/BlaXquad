Feature: Retiring failed and partial provider startup

  A real squad-hq process whose provider fails during startup - whether before its runtime ever becomes available,
  or partway through establishing role sessions - reports a clear provider diagnostic on standard error (never a
  raw ".NET Unhandled exception" dump), exits with a non-zero code, disposes every session already started,
  never starts a role it had not yet reached, never leaves a live Headquarters instance behind, preserves durable
  workspace state it does not own, and permits a fresh, healthy launch against the same project afterward - proven
  only through the real process, the real "squad-hq wait-for-agent" Headquarters-control command, and the
  fake-provider control pipe, never through SquadApplication directly.

  Scenario: Provider failure before its runtime becomes available reports a clean diagnostic and leaves no live Headquarters instance

    Given `blaxquad/squad.json` configures:
      | role  |
      | coder |
    And Headquarters' provider fails before its runtime becomes available
    And role "coder" has a durable file "notes.md" containing "Keep this note."

    When the operator launches Headquarters
    And Headquarters' process exits on its own

    Then Headquarters exits with a non-zero code
    And Headquarters' standard error contains "Provider startup failed"
    And Headquarters' standard error does not contain "Unhandled exception"
    And the operator finds Headquarters unavailable for role "coder"
    And role "coder"'s durable file "notes.md" still contains "Keep this note."

    When the operator launches a new Headquarters against the same project

    Then the new Headquarters process reports ready

  Scenario: Provider failure partway through session startup disposes started sessions and never starts the rest

    Given `blaxquad/squad.json` configures:
      | role     |
      | coder    |
      | reviewer |
    And Headquarters' provider fails after 1 session has started
    And role "coder" has a durable file "notes.md" containing "Keep this note."

    When the operator launches Headquarters

    Then Headquarters starts an agent session for role "coder"
    And Headquarters disposes the agent session for role "coder"
    And Headquarters never starts an agent session for role "reviewer"

    When Headquarters' process exits on its own

    Then Headquarters exits with a non-zero code
    And Headquarters' standard error contains "Provider startup failed"
    And Headquarters' standard error does not contain "Unhandled exception"
    And the operator finds Headquarters unavailable for role "coder"
    And role "coder"'s durable file "notes.md" still contains "Keep this note."

    When the operator launches a new Headquarters against the same project

    Then the new Headquarters process reports ready
