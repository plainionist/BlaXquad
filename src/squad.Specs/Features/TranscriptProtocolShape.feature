Feature: Transcript protocol shape and entry sources

  The real UI JSON protocol reports every supported transcript entry source through the dashboard-compatible
  fields the frontend relies on - source, content, sequence, operation, and entry index - both as incremental
  "transcript.update" messages and as a full "transcript.synchronize" message. These scenarios drive the published
  squad-hq process with the fake provider and observe only the real protocol through the headless UI client and
  the fake-provider control pipe; step definitions never inspect production transcript storage.

  Scenario: Transcript updates report every supported entry source with dashboard protocol fields
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    And the backend scenario observes a transcript update for role "coder" with source "harness"
    When the backend scenario sends the prompt "What should I build?" to role "coder"
    Then the "coder" agent observes the prompt "What should I build?"
    And the backend scenario observes a transcript update for role "coder" with source "user" and content "What should I build?"
    When the "coder" agent replies with "Build the driver."
    Then the backend scenario observes a transcript update for role "coder" with source "assistant" and content "Build the driver."
    When the "coder" agent emits a final reasoning message "Considering the request."
    Then the backend scenario observes a transcript update for role "coder" with source "reasoning" and content "Considering the request."
    When the "coder" agent emits a system message "Context compaction started"
    Then the backend scenario observes a transcript update for role "coder" with source "system" and content "Context compaction started"
    And every observed transcript update for role "coder" reports a strictly increasing sequence and entry index
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero

  Scenario: Transcript synchronization reports every supported entry source with dashboard protocol fields
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    When the backend scenario sends the prompt "What should I build?" to role "coder"
    Then the "coder" agent observes the prompt "What should I build?"
    When the "coder" agent replies with "Build the driver."
    And the "coder" agent emits a final reasoning message "Considering the request."
    And the "coder" agent emits a system message "Context compaction started"
    Then the backend scenario observes a transcript update for role "coder" with source "system" and content "Context compaction started"
    When the backend scenario requests a fresh transcript synchronization
    Then the transcript synchronization for role "coder" includes a "harness" entry
    And the transcript synchronization for role "coder" includes a "user" entry "What should I build?"
    And the transcript synchronization for role "coder" includes a "assistant" entry "Build the driver."
    And the transcript synchronization for role "coder" includes a "reasoning" entry "Considering the request."
    And the transcript synchronization for role "coder" includes a "system" entry "Context compaction started"
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero
