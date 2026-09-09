Feature: Terminated session event suppression

  Once a provider session has terminated - either by completing gracefully or by failing - any further work
  published from that same session identity is delayed, stale work: a late assistant message, a late readiness
  change, a late interaction request, and a late second termination all arrive after the role's protocol state is
  already final. None of that delayed publication may resurrect the role, change its transcript, or open a new
  pending interaction, and a healthy sibling role must keep handling prompts undisturbed. A stale publisher also
  must not obstruct an otherwise normal host-control shutdown - proven only through the real process, the real
  "squad-hq shutdown" host-control command, and the fake-provider control pipe, never through SquadViewModel or
  SquadApplication directly.

  Background:
    Given a backend scenario configured with roles "coder,reviewer"
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    And the backend scenario observes a session started for role "reviewer" across the control pipe

  Scenario: Delayed events from a gracefully completed session cannot mutate later published state
    When the "coder" agent requests permission "permission-1" with description "Run the deploy script?"
    Then the backend scenario observes a pending permission "permission-1" for role "coder" with description "Run the deploy script?"
    When the "coder" agent completes its session
    Then the backend scenario observes role "coder" at status "stopped"
    When the "coder" agent emits a final assistant message "should not resurrect the role"
    Then the backend scenario does not observe the transcript for role "coder" containing "should not resurrect the role" within 2 seconds
    When the "coder" agent requests permission "permission-2" with description "Late request?"
    Then the backend scenario observes no pending permission "permission-2" for role "coder"
    When the "coder" agent emits readiness "ready"
    And the "coder" agent fails its session with message "late failure after stop"
    Then the backend scenario observes role "coder" at status "stopped"
    When the backend scenario requests a fresh transcript synchronization
    Then the backend scenario does not observe the transcript for role "coder" containing "late failure after stop" within 2 seconds
    When the backend scenario sends the prompt "still available" to role "reviewer"
    Then the "reviewer" agent observes the prompt "still available"

  Scenario: Delayed events from a failed session cannot mutate later published state
    When the "coder" agent requests permission "permission-1" with description "Deploy to prod?"
    Then the backend scenario observes a pending permission "permission-1" for role "coder" with description "Deploy to prod?"
    When the "coder" agent fails its session with message "event channel overloaded"
    Then the backend scenario observes role "coder" at status "error"
    And the backend scenario observes no pending permission "permission-1" for role "coder"
    When the "coder" agent emits a final assistant message "should not resurrect the role"
    Then the backend scenario does not observe the transcript for role "coder" containing "should not resurrect the role" within 2 seconds
    When the "coder" agent requests permission "permission-2" with description "Late request?"
    Then the backend scenario observes no pending permission "permission-2" for role "coder"
    When the "coder" agent emits readiness "ready"
    And the "coder" agent completes its session
    Then the backend scenario observes role "coder" at status "error"
    When the backend scenario requests a fresh transcript synchronization
    Then the backend scenario does not observe the transcript for role "coder" containing "should not resurrect the role" within 2 seconds
    When the backend scenario sends the prompt "still available" to role "reviewer"
    Then the "reviewer" agent observes the prompt "still available"

  Scenario: A stale publisher from a terminated session cannot obstruct normal shutdown
    When the "coder" agent completes its session
    Then the backend scenario observes role "coder" at status "stopped"
    When the "coder" agent emits a final assistant message "still coming after stop"
    And the "coder" agent emits readiness "ready"
    And the "coder" agent fails its session with message "stale failure"
    And the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero
    And the backend scenario observes a session disposed for role "coder" across the control pipe
    And the backend scenario confirms host control is unavailable for role "coder"
