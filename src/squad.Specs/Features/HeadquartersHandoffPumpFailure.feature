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
    Given a backend scenario configured with roles "coder"
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    And the backend scenario seeds ".blaxquad/handoffs/inbox/new/queued.handoff" into role "coder"'s worktree with content "Do not lose this queued handoff."
    When the backend scenario poisons role "coder"'s handoff outbox directory
    And the backend scenario waits for the process to exit on its own
    Then the backend scenario observes a non-zero exit code
    And the backend scenario observes standard error containing "Handoff delivery failed"
    And the backend scenario observes standard error does not contain "Unhandled exception"
    And the backend scenario observes standard error does not contain "Provider startup failed"
    And the backend scenario observes a session disposed for role "coder" across the control pipe
    And the backend scenario confirms host control is unavailable for role "coder"
    And the backend scenario observes role "coder"'s seeded ".blaxquad/handoffs/inbox/new/queued.handoff" still contains "Do not lose this queued handoff."
    When the backend scenario repairs role "coder"'s handoff outbox directory
    And a fresh backend scenario starts squad-hq against the same workspace with the echo provider fixture
    Then the fresh backend scenario reports the process as ready
