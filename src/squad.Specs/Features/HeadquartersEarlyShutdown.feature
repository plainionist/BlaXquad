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
    Given a backend scenario configured with roles "coder"
    And the backend scenario has enabled the fake-provider control transport
    And the backend scenario seeds "notes.md" into role "coder"'s worktree with content "Keep this note."
    When the backend scenario launches squad-hq with the fake provider fixture without completing the ready handshake
    And the backend scenario starts watching for role "coder" to become ready
    And the backend scenario requests a host-control shutdown as soon as it is reachable while sending the prompt "too late" to role "coder"
    Then the backend scenario observes an exit code of zero
    And the backend scenario observes role "coder" was never reported ready
    And the backend scenario observes no session was ever started for role "coder"
    And the backend scenario observes role "coder" received no prompt
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
    And the backend scenario observes no session was ever started for role "reviewer"
    When the backend scenario starts watching for role "coder" to become ready
    And the backend scenario requests a host-control shutdown while sending the prompt "too late" to role "reviewer"
    Then the backend scenario observes an exit code of zero
    And the backend scenario observes role "coder" was never reported ready
    And the backend scenario observes a session disposed for role "coder" across the control pipe
    And the backend scenario observes no session was ever started for role "reviewer"
    And the backend scenario observes role "reviewer" received no prompt
    And the backend scenario confirms host control is unavailable for role "coder"
    And the backend scenario observes role "coder"'s seeded "notes.md" still contains "Keep this note."
    When a fresh backend scenario starts squad-hq against the same workspace with the echo provider fixture
    Then the fresh backend scenario reports the process as ready
