Feature: Retiring failed and partial provider startup

  A real squad-hq process whose provider fails during startup - whether before its runtime ever becomes available,
  or partway through establishing role sessions - reports a clear provider diagnostic on standard error (never a
  raw ".NET Unhandled exception" dump), exits with a non-zero code, disposes every session already started,
  never starts a role it had not yet reached, never leaves a live host behind, preserves durable workspace state
  it does not own, and permits a fresh, healthy launch against the same project afterward - proven only through
  the real process, the real "squad-hq wait-for-agent" host-control command, and the fake-provider control pipe,
  never through SquadApplication directly.

  Scenario: Provider failure before its runtime becomes available reports a clean diagnostic and leaves no live host
    Given a backend scenario configured with roles "coder"
    And the backend scenario configures the fake provider to fail before its runtime becomes available
    And the backend scenario seeds "notes.md" into role "coder"'s worktree with content "Keep this note."
    When the backend scenario starts squad-hq with the fake provider fixture
    And the backend scenario waits for the process to exit on its own
    Then the backend scenario observes a non-zero exit code
    And the backend scenario observes standard error containing "Provider startup failed"
    And the backend scenario observes standard error does not contain "Unhandled exception"
    And the backend scenario confirms host control is unavailable for role "coder"
    And the backend scenario observes role "coder"'s seeded "notes.md" still contains "Keep this note."
    When a fresh backend scenario starts squad-hq against the same workspace with the echo provider fixture
    Then the fresh backend scenario reports the process as ready

  Scenario: Provider failure partway through session startup disposes started sessions and never starts the rest
    Given a backend scenario configured with roles "coder,reviewer"
    And the backend scenario has enabled the fake-provider control transport
    And the backend scenario configures the fake provider to fail after 1 session has started
    And the backend scenario seeds "notes.md" into role "coder"'s worktree with content "Keep this note."
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    And the backend scenario observes a session disposed for role "coder" across the control pipe
    And the backend scenario observes no session was ever started for role "reviewer"
    When the backend scenario waits for the process to exit on its own
    Then the backend scenario observes a non-zero exit code
    And the backend scenario observes standard error containing "Provider startup failed"
    And the backend scenario observes standard error does not contain "Unhandled exception"
    And the backend scenario confirms host control is unavailable for role "coder"
    And the backend scenario observes role "coder"'s seeded "notes.md" still contains "Keep this note."
    When a fresh backend scenario starts squad-hq against the same workspace with the echo provider fixture
    Then the fresh backend scenario reports the process as ready
