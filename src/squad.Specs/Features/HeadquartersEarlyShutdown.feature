Feature: Stopping safely before and during startup

  A real squad-hq process that receives a host-control shutdown request at any point before it reaches readiness -
  whether requested as early as the process can possibly be reached, or while provider startup is genuinely
  paused part-way through registering role sessions - terminates cleanly, disposes every session already
  registered, preserves durable workspace state it does not own, releases every externally observable resource,
  and permits a fresh, healthy launch against the same project afterward - proven only through the real process,
  the real "squad-hq shutdown" host-control command, and the fake-provider control pipe, never through
  SquadApplication or a lifecycle trace.

  Scenario: Shutdown requested as early as possible prevents startup from ever completing
    Given a backend scenario configured with roles "coder"
    And the backend scenario seeds "notes.md" into role "coder"'s worktree with content "Keep this note."
    When the backend scenario launches squad-hq without completing the ready handshake
    And the backend scenario requests a host-control shutdown as soon as it is reachable
    Then the backend scenario observes an exit code of zero
    And the backend scenario confirms host control is unavailable for role "coder"
    And the backend scenario observes role "coder"'s seeded "notes.md" still contains "Keep this note."
    When a fresh backend scenario starts squad-hq against the same workspace with the echo provider fixture
    Then the fresh backend scenario reports the process as ready

  Scenario: Shutdown requested while provider startup is paused disposes the already-started session
    Given a backend scenario configured with roles "coder,reviewer"
    And the backend scenario has enabled the fake-provider control transport
    And the backend scenario gates provider startup after 1 session has started
    And the backend scenario seeds "notes.md" into role "coder"'s worktree with content "Keep this note."
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero
    And the backend scenario observes a session disposed for role "coder" across the control pipe
    And the backend scenario confirms host control is unavailable for role "coder"
    And the backend scenario observes role "coder"'s seeded "notes.md" still contains "Keep this note."
    When a fresh backend scenario starts squad-hq against the same workspace with the echo provider fixture
    Then the fresh backend scenario reports the process as ready
