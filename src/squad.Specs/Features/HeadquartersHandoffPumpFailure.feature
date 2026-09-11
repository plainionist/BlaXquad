Feature: Surfacing a real handoff-pump failure after readiness

  A real squad-hq process proves that an unexpected handoff-pump failure - a genuine filesystem fault reached
  through the real, filesystem-polling delivery pump, never an injected IHandoffPump.Failure or a directly
  constructed SquadApplication - terminates headquarters visibly: it reports the pump's own diagnostic on
  standard error (never a raw ".NET Unhandled exception" dump, and never mislabeled as a startup failure), exits
  with a non-zero code, disposes every session already started, releases the host completely, preserves a
  durable, already-queued handoff artifact it does not own, and still permits a fresh, healthy launch against the
  same project afterward - proven only through the real process, the real "squad-hq wait-for-agent" host-control
  command, and a real filesystem fault, never through SquadApplication directly.

  Scenario: A real handoff-pump failure after readiness stops the host with its own diagnostic
    Given `blaxquad/squad.json` configures:
      | role  |
      | coder |
    When the operator launches Headquarters
    Then Headquarters starts an agent session for role "coder"
    When role "coder" has a durable file ".blaxquad/handoffs/inbox/new/queued.handoff.json" containing "Do not lose this queued handoff."
    And role "coder"'s handoff outbox directory is poisoned
    And Headquarters' process exits on its own
    Then Headquarters exits with a non-zero code
    And Headquarters' standard error contains "Handoff delivery failed"
    And Headquarters' standard error does not contain "Unhandled exception"
    And Headquarters' standard error does not contain "Provider startup failed"
    And Headquarters disposes the agent session for role "coder"
    And the operator finds Headquarters unavailable for role "coder"
    And role "coder"'s durable file ".blaxquad/handoffs/inbox/new/queued.handoff.json" still contains "Do not lose this queued handoff."
    When role "coder"'s handoff outbox directory is repaired
    And the operator launches a new Headquarters against the same project
    Then the new Headquarters process reports ready
