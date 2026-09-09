Feature: Assistant and reasoning stream finalization

  Assistant and reasoning deltas aggregate into one transcript entry, an interleaved snapshot does not end that
  aggregation, a final message replaces the streamed draft without disturbing earlier entries, and an idle
  transition ends a reasoning stream before its next delta starts a new entry. These scenarios drive the published
  squad-hq process with the fake provider and observe only the real protocol through the headless UI client and
  the fake-provider control pipe; step definitions never inspect production transcript storage.

  Scenario: Consecutive assistant deltas aggregate into one entry despite a snapshot mid-stream, and the final message replaces the draft without losing earlier entries
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    When the backend scenario sends the prompt "What should I build?" to role "coder"
    Then the "coder" agent observes the prompt "What should I build?"
    And the backend scenario observes a transcript update for role "coder" with source "user" and content "What should I build?"
    When the "coder" agent emits an assistant delta "Hello "
    Then the backend scenario observes a transcript update for role "coder" with source "assistant" and content "Hello "
    When the backend scenario requests a fresh transcript synchronization
    Then the transcript synchronization for role "coder" includes an entry with source "assistant" and content "Hello "
    When the "coder" agent emits an assistant delta "world"
    Then the backend scenario observes a transcript update for role "coder" with operation "append-content" and content "world"
    And the most recently observed transcript updates for role "coder" report the same entry index
    When the "coder" agent emits a final assistant message "Hello world"
    Then the backend scenario observes a transcript update for role "coder" with operation "replace" and content "Hello world"
    And the most recently observed transcript updates for role "coder" report the same entry index
    When the backend scenario requests a fresh transcript synchronization
    Then the transcript synchronization for role "coder" includes exactly these entries:
      | source    | content              |
      | user      | What should I build? |
      | assistant | Hello world          |
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero

  Scenario: A final reasoning message replaces the streamed draft without losing the earlier entry
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    When the backend scenario sends the prompt "What should I build?" to role "coder"
    Then the "coder" agent observes the prompt "What should I build?"
    And the backend scenario observes a transcript update for role "coder" with source "user" and content "What should I build?"
    When the "coder" agent emits a reasoning delta "draft"
    Then the backend scenario observes a transcript update for role "coder" with source "reasoning" and content "draft"
    When the "coder" agent emits a final reasoning message "final"
    Then the backend scenario observes a transcript update for role "coder" with operation "replace" and content "final"
    And the most recently observed transcript updates for role "coder" report the same entry index
    When the backend scenario requests a fresh transcript synchronization
    Then the transcript synchronization for role "coder" includes exactly these entries:
      | source    | content               |
      | user      | What should I build? |
      | reasoning | final                 |
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero

  Scenario: Idle finalizes a reasoning stream before the next delta starts a new entry
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    When the "coder" agent emits a reasoning delta "first"
    Then the backend scenario observes a transcript update for role "coder" with source "reasoning" and content "first"
    When the "coder" agent emits idle
    And the "coder" agent emits a reasoning delta "second"
    Then the backend scenario observes a transcript update for role "coder" with source "reasoning" and content "second"
    And the most recently observed transcript updates for role "coder" report different entry indices
    When the backend scenario requests a fresh transcript synchronization
    Then the transcript synchronization for role "coder" includes exactly these entries:
      | source    | content |
      | reasoning | first   |
      | reasoning | second  |
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero
