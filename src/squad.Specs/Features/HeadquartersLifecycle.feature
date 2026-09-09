Feature: Healthy headquarters lifecycle

  A real multi-role squad-hq process reaches UI and agent readiness, shuts down cleanly through its public
  host-control command, preserves durable workspace state it does not own, releases every externally observable
  resource, and permits a fresh, healthy launch against the same project afterward - proven only through the real
  process, the real UI protocol, the real "squad-hq wait-for-agent" and "squad-hq shutdown" host-control commands,
  and the fake-provider control pipe, never through SquadApplication, a host lease object, or a lifecycle trace.

  Scenario: A healthy multi-role headquarters process reaches full readiness and shuts down through host control
    Given a backend scenario configured with roles "coder,reviewer"
    And the backend scenario has enabled the fake-provider control transport
    And the backend scenario seeds "notes.md" into role "coder"'s worktree with content "Keep this note."
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    And the backend scenario observes a session started for role "reviewer" across the control pipe
    And the "coder" agent observes a harness message
    And the "reviewer" agent observes a harness message
    When the "coder" agent emits idle
    And the "reviewer" agent emits idle
    Then the backend scenario confirms role "coder" is ready through squad-hq wait-for-agent
    And the backend scenario confirms role "reviewer" is ready through squad-hq wait-for-agent
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero
    And the backend scenario observes a session disposed for role "coder" across the control pipe
    And the backend scenario observes a session disposed for role "reviewer" across the control pipe
    And the backend scenario confirms host control is unavailable for role "coder"
    And the backend scenario observes role "coder"'s seeded "notes.md" still contains "Keep this note."
    When a fresh backend scenario starts squad-hq against the same workspace with the echo provider fixture
    Then the fresh backend scenario reports the process as ready
