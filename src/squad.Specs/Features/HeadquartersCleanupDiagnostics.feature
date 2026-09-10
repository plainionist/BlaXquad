Feature: Preserving primary and cleanup diagnostics through final release

  A real squad-hq process whose provider primary operation - startup, or after readiness a fatal backend-wide
  runtime failure - fails independently of its own provider cleanup stage still reports every distinct
  diagnostic on standard error (never a raw ".NET Unhandled exception" dump) with a non-zero exit, continues
  cleanup through every owned resource despite the cleanup failure, preserves durable workspace state it does
  not own, and permits a fresh, healthy launch against the same project afterward. Independently, provider
  cleanup held at its own acknowledged boundary keeps the host genuinely owned - answering a real host-control
  command - only until that cleanup actually finishes, releasing ownership no earlier - proven only through the
  real process, the real "squad-hq shutdown" and "squad-hq wait-for-agent" host-control commands, and the
  fake-provider control pipe, never through SquadApplication directly.

  Scenario: A startup failure combined with an independent cleanup failure surfaces both diagnostics
    Given a backend scenario configured with roles "coder"
    And the backend scenario has enabled the fake-provider control transport
    And the backend scenario configures the fake provider to fail after 1 session has started
    And the backend scenario configures the fake provider to fail its cleanup with message "cleanup boundary failed"
    And the backend scenario seeds "notes.md" into role "coder"'s worktree with content "Keep this note."
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    And the backend scenario observes a session disposed for role "coder" across the control pipe
    When the backend scenario waits for the process to exit on its own
    Then the backend scenario observes a non-zero exit code
    And the backend scenario observes standard error containing "fake provider failed after starting 1 session(s)"
    And the backend scenario observes standard error containing "cleanup boundary failed"
    And the backend scenario observes standard error does not contain "Unhandled exception"
    And the backend scenario confirms host control is unavailable for role "coder"
    And the backend scenario observes role "coder"'s seeded "notes.md" still contains "Keep this note."
    When a fresh backend scenario starts squad-hq against the same workspace with the echo provider fixture
    Then the fresh backend scenario reports the process as ready

  Scenario: A runtime failure after readiness combined with an independent cleanup failure surfaces both diagnostics
    Given a backend scenario configured with roles "coder"
    And the backend scenario has enabled the fake-provider control transport
    And the backend scenario configures the fake provider to fail its cleanup with message "cleanup boundary failed"
    And the backend scenario seeds "notes.md" into role "coder"'s worktree with content "Keep this note."
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    When the backend scenario fails the fake provider's backend with message "shared SDK force-stop failed"
    And the backend scenario waits for the process to exit on its own
    Then the backend scenario observes a non-zero exit code
    And the backend scenario observes standard error containing "shared SDK force-stop failed"
    And the backend scenario observes standard error containing "cleanup boundary failed"
    And the backend scenario observes standard error does not contain "Unhandled exception"
    And the backend scenario observes a session disposed for role "coder" across the control pipe
    And the backend scenario confirms host control is unavailable for role "coder"
    And the backend scenario observes role "coder"'s seeded "notes.md" still contains "Keep this note."
    When a fresh backend scenario starts squad-hq against the same workspace with the echo provider fixture
    Then the fresh backend scenario reports the process as ready

  Scenario: Provider cleanup held at its acknowledged boundary keeps the host owned until it completes
    Given a backend scenario configured with roles "coder"
    And the backend scenario has enabled the fake-provider control transport
    And the backend scenario seeds "notes.md" into role "coder"'s worktree with content "Keep this note."
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    When the "coder" agent emits idle
    Then the backend scenario observes role "coder" at status "idle"
    When the backend scenario arms role "coder" to hold its next session disposal pending
    And the backend scenario requests a host-control shutdown without waiting for the process to exit
    Then the backend scenario observes role "coder"'s session disposal held
    And the backend scenario confirms host control is still available for role "coder"
    When the backend scenario completes the pending session disposal for role "coder"
    And the backend scenario waits for the process to exit on its own
    Then the backend scenario observes an exit code of zero
    And the backend scenario observes a session disposed for role "coder" across the control pipe
    And the backend scenario confirms host control is unavailable for role "coder"
    And the backend scenario observes role "coder"'s seeded "notes.md" still contains "Keep this note."
    When a fresh backend scenario starts squad-hq against the same workspace with the echo provider fixture
    Then the fresh backend scenario reports the process as ready
