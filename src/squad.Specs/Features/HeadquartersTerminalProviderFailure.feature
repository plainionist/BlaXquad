Feature: Surfacing terminal provider failures after readiness

  A real squad-hq process distinguishes a per-session provider failure, which affects only its own role and never
  stops the host, from a fatal, backend-wide provider failure, which reports a clear diagnostic on standard error
  (never a raw ".NET Unhandled exception" dump), exits with a non-zero code, disposes every session already
  started, never leaves a live host behind, preserves durable workspace state it does not own, remains the
  reported outcome even when a normal shutdown is requested around the same time, and permits a fresh, healthy
  launch against the same project afterward - proven only through the real process, the real "squad-hq shutdown"
  and "squad-hq wait-for-agent" host-control commands, and the fake-provider control pipe, never through
  SquadApplication directly.

  Scenario: A per-session failure after readiness marks only that role's terminal state and never stops the host
    Given a backend scenario configured with roles "coder,reviewer"
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    And the backend scenario observes a session started for role "reviewer" across the control pipe
    When the "coder" agent fails its session with message "SDK unavailable"
    Then the backend scenario observes role "coder" at status "error"
    When the backend scenario sends the prompt "still available" to role "reviewer"
    Then the "reviewer" agent observes the prompt "still available"
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero
    And the backend scenario observes a session disposed for role "coder" across the control pipe
    And the backend scenario observes a session disposed for role "reviewer" across the control pipe

  Scenario: A backend-wide terminal failure after readiness stops the host with the original diagnostic
    Given a backend scenario configured with roles "coder"
    And the backend scenario has enabled the fake-provider control transport
    And the backend scenario seeds "notes.md" into role "coder"'s worktree with content "Keep this note."
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    When the backend scenario fails the fake provider's backend with message "shared SDK force-stop failed"
    And the backend scenario waits for the process to exit on its own
    Then the backend scenario observes a non-zero exit code
    And the backend scenario observes standard error containing "shared SDK force-stop failed"
    And the backend scenario observes standard error does not contain "Unhandled exception"
    And the backend scenario observes standard error does not contain "Provider startup failed"
    And the backend scenario observes a session disposed for role "coder" across the control pipe
    And the backend scenario confirms host control is unavailable for role "coder"
    And the backend scenario observes role "coder"'s seeded "notes.md" still contains "Keep this note."
    When a fresh backend scenario starts squad-hq against the same workspace with the echo provider fixture
    Then the fresh backend scenario reports the process as ready

  Scenario: A backend-wide terminal failure remains the reported outcome even when shutdown is also requested
    Given a backend scenario configured with roles "coder"
    And the backend scenario has enabled the fake-provider control transport
    And the backend scenario seeds "notes.md" into role "coder"'s worktree with content "Keep this note."
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    When the backend scenario fails the fake provider's backend with message "shared SDK force-stop failed"
    And the backend scenario requests a host-control shutdown
    Then the backend scenario observes a non-zero exit code
    And the backend scenario observes standard error containing "shared SDK force-stop failed"
    And the backend scenario observes standard error does not contain "Unhandled exception"
    And the backend scenario observes standard error does not contain "Provider startup failed"
    And the backend scenario observes a session disposed for role "coder" across the control pipe
    And the backend scenario confirms host control is unavailable for role "coder"
    And the backend scenario observes role "coder"'s seeded "notes.md" still contains "Keep this note."
    When a fresh backend scenario starts squad-hq against the same workspace with the echo provider fixture
    Then the fresh backend scenario reports the process as ready
