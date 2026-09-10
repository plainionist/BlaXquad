Feature: Transcript protocol shape and entry sources

  The real UI JSON protocol reports every supported transcript entry source through the dashboard-compatible
  fields the frontend relies on - source, content, sequence, operation, and entry index - both as incremental
  "transcript.update" messages and as a full "transcript.synchronize" message.

  Background:
    Given `blaxquad/squad.json` configures:
      | role  |
      | coder |
    When the operator launches Headquarters
    Then Headquarters starts an agent session for role "coder"

  Scenario: Transcript updates report every supported entry source with dashboard protocol fields
    Then the dashboard receives a transcript update for role "coder" with source "harness" and content "Session started."
    When the user sends "What should I build?" to role "coder"
    Then the "coder" agent observes the prompt "What should I build?"
    And the dashboard receives a transcript update for role "coder" with source "user" and content "What should I build?"
    When the "coder" agent replies with "Build the driver."
    Then the dashboard receives a transcript update for role "coder" with source "assistant" and content "Build the driver."
    When the "coder" agent emits a final reasoning message "Considering the request."
    Then the dashboard receives a transcript update for role "coder" with source "reasoning" and content "Considering the request."
    When the "coder" agent emits a system message "Context compaction started"
    Then the dashboard receives a transcript update for role "coder" with source "system" and content "Context compaction started"
    And every transcript update received for role "coder" reports a strictly increasing sequence and entry index
    When the operator shuts down Headquarters
    Then Headquarters exits with code 0

  Scenario: Transcript synchronization reports every supported entry source with dashboard protocol fields
    When the user sends "What should I build?" to role "coder"
    Then the "coder" agent observes the prompt "What should I build?"
    When the "coder" agent replies with "Build the driver."
    And the "coder" agent emits a final reasoning message "Considering the request."
    And the "coder" agent emits a system message "Context compaction started"
    Then the dashboard receives a transcript update for role "coder" with source "system" and content "Context compaction started"
    When the user requests a fresh transcript synchronization for role "coder"
    Then the transcript synchronization for role "coder" reports every supported entry source with dashboard protocol fields:
      | source    | content                     |
      | harness   | Session started.            |
      | user      | What should I build?        |
      | assistant | Build the driver.           |
      | reasoning | Considering the request.    |
      | system    | Context compaction started  |
    When the operator shuts down Headquarters
    Then Headquarters exits with code 0
