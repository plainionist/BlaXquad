Feature: Preserving primary and cleanup diagnostics through final release

  A real squad-hq process whose provider primary operation - startup, or after readiness a fatal backend-wide
  runtime failure - fails independently of its own provider cleanup stage still reports every distinct
  diagnostic on standard error (never a raw ".NET Unhandled exception" dump) with a non-zero exit, continues
  cleanup through every owned resource despite the cleanup failure, preserves durable workspace state it does
  not own, and permits a fresh, healthy launch against the same project afterward. Independently, provider
  cleanup held at its own acknowledged boundary keeps Headquarters genuinely owned - answering a real
  Headquarters-control command - only until that cleanup actually finishes, releasing ownership no earlier -
  proven only through the real process, the real "squad-hq shutdown" and "squad-hq wait-for-agent"
  Headquarters-control commands, and the fake-provider control pipe, never through SquadApplication directly.

  Scenario: A startup failure combined with an independent cleanup failure surfaces both diagnostics

    Given `blaxquad/squad.json` configures:
      | role  |
      | coder |
    And Headquarters' provider fails after 1 session has started
    And Headquarters' provider fails its cleanup with message "cleanup boundary failed"
    And role "coder" has a durable file "notes.md" containing "Keep this note."

    When the operator launches Headquarters

    Then Headquarters starts an agent session for role "coder"
    And Headquarters disposes the agent session for role "coder"

    When Headquarters' process exits on its own

    Then Headquarters exits with a non-zero code
    And Headquarters' standard error contains "fake provider failed after starting 1 session(s)"
    And Headquarters' standard error contains "cleanup boundary failed"
    And Headquarters' standard error does not contain "Unhandled exception"
    And the operator finds Headquarters unavailable for role "coder"
    And role "coder"'s durable file "notes.md" still contains "Keep this note."

    When the operator launches a new Headquarters against the same project

    Then the new Headquarters process reports ready

  Scenario: A runtime failure after readiness combined with an independent cleanup failure surfaces both diagnostics

    Given `blaxquad/squad.json` configures:
      | role  |
      | coder |
    And Headquarters' provider fails its cleanup with message "cleanup boundary failed"
    And role "coder" has a durable file "notes.md" containing "Keep this note."

    When the operator launches Headquarters

    Then Headquarters starts an agent session for role "coder"

    When the agent provider fails its backend with message "shared SDK force-stop failed"
    And Headquarters' process exits on its own

    Then Headquarters exits with a non-zero code
    And Headquarters' standard error contains "shared SDK force-stop failed"
    And Headquarters' standard error contains "cleanup boundary failed"
    And Headquarters' standard error does not contain "Unhandled exception"
    And Headquarters disposes the agent session for role "coder"
    And the operator finds Headquarters unavailable for role "coder"
    And role "coder"'s durable file "notes.md" still contains "Keep this note."

    When the operator launches a new Headquarters against the same project

    Then the new Headquarters process reports ready

  Scenario: Provider cleanup held at its acknowledged boundary keeps Headquarters owned until it completes

    Given `blaxquad/squad.json` configures:
      | role  |
      | coder |
    And role "coder" has a durable file "notes.md" containing "Keep this note."

    When the operator launches Headquarters

    Then Headquarters starts an agent session for role "coder"

    When the "coder" agent emits idle

    Then the dashboard shows role "coder" at status "idle"

    When the "coder" agent holds its next session disposal pending
    And the operator begins shutting down Headquarters without waiting for it to exit

    Then the "coder" agent's session disposal is held
    And the operator finds Headquarters still available for role "coder"

    When the "coder" agent releases its pending session disposal
    And Headquarters' process exits on its own

    Then Headquarters exits with code 0
    And Headquarters disposes the agent session for role "coder"
    And the operator finds Headquarters unavailable for role "coder"
    And role "coder"'s durable file "notes.md" still contains "Keep this note."

    When the operator launches a new Headquarters against the same project

    Then the new Headquarters process reports ready
