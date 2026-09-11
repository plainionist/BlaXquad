Feature: Terminal session finality

  A terminal session failure or a graceful completion makes a role's status final and prevents further mutation of
  its published state: once terminal, a role's pending interactions are removed, later commands addressed to it are
  rejected, and any further work published under that same session identity - a late reply, a late idle transition,
  a late interaction request, or a late second termination - never resurrects the role, changes its
  transcript, or opens a new pending interaction, whether the transcript is checked as it stands or refreshed
  through a later synchronization. A terminal role never prevents a healthy sibling role from continuing to handle
  prompts, and it never obstructs an otherwise normal shutdown through Headquarters.

  Background:
    Given `blaxquad/squad.json` configures:
      | role     |
      | coder    |
      | reviewer |
    When the operator launches Headquarters
    Then Headquarters starts an agent session for role "coder"
    And Headquarters starts an agent session for role "reviewer"

  Scenario: A terminal session failure affects only its role and rejects later commands
    When the "coder" agent requests permission "permission-1" with description "Deploy to prod?"
    Then the dashboard shows a pending permission "permission-1" for role "coder" with description "Deploy to prod?"
    When the "coder" agent fails its session with message "event channel overloaded"
    Then the dashboard shows role "coder" at status "error"
    And the dashboard shows no pending permission "permission-1" for role "coder"
    When the "coder" agent emits the reasoning "should not resurrect the role"
    Then the transcript for role "coder" does not contain "should not resurrect the role" within 2 seconds
    When the user sends "still available" to role "reviewer"
    Then the "reviewer" agent observes the prompt "still available"
    When the "reviewer" agent replies with "reviewer done"
    Then the transcript for role "reviewer" contains "reviewer done"
    When the user sends "too late" to role "coder"
    Then the user observes a protocol error mentioning "unavailable"

  Scenario: Delayed events from a gracefully completed session cannot mutate later published state
    When the "coder" agent requests permission "permission-1" with description "Run the deploy script?"
    Then the dashboard shows a pending permission "permission-1" for role "coder" with description "Run the deploy script?"
    When the "coder" agent completes its session
    Then the dashboard shows role "coder" at status "stopped"
    When the "coder" agent emits a final assistant message "should not resurrect the role"
    Then the transcript for role "coder" does not contain "should not resurrect the role" within 2 seconds
    When the "coder" agent requests permission "permission-2" with description "Late request?"
    Then the dashboard shows no pending permission "permission-2" for role "coder"
    When the "coder" agent emits idle
    And the "coder" agent fails its session with message "late failure after stop"
    Then the dashboard shows role "coder"'s latest status as "stopped"
    When the user requests a fresh transcript synchronization for role "coder"
    Then the freshly synchronized transcript for role "coder" does not contain "late failure after stop"
    When the user sends "still available" to role "reviewer"
    Then the "reviewer" agent observes the prompt "still available"

  Scenario: Delayed events from a failed session cannot mutate later published state
    When the "coder" agent requests permission "permission-1" with description "Deploy to prod?"
    Then the dashboard shows a pending permission "permission-1" for role "coder" with description "Deploy to prod?"
    When the "coder" agent fails its session with message "event channel overloaded"
    Then the dashboard shows role "coder" at status "error"
    And the dashboard shows no pending permission "permission-1" for role "coder"
    When the "coder" agent emits a final assistant message "should not resurrect the role"
    Then the transcript for role "coder" does not contain "should not resurrect the role" within 2 seconds
    When the "coder" agent requests permission "permission-2" with description "Late request?"
    Then the dashboard shows no pending permission "permission-2" for role "coder"
    When the "coder" agent emits idle
    And the "coder" agent completes its session
    Then the dashboard shows role "coder"'s latest status as "error"
    When the user requests a fresh transcript synchronization for role "coder"
    Then the freshly synchronized transcript for role "coder" does not contain "should not resurrect the role"
    When the user sends "still available" to role "reviewer"
    Then the "reviewer" agent observes the prompt "still available"

  Scenario: A stale publisher from a terminated session cannot obstruct normal shutdown
    When the "coder" agent completes its session
    Then the dashboard shows role "coder" at status "stopped"
    When the "coder" agent emits a final assistant message "still coming after stop"
    And the "coder" agent emits idle
    And the "coder" agent fails its session with message "stale failure"
    And the operator shuts down Headquarters
    Then Headquarters exits with code 0
    And Headquarters disposes the agent session for role "coder"
    And the operator finds Headquarters unavailable for role "coder"
