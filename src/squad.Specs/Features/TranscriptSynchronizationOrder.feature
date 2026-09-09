Feature: Transcript synchronization order

  A reconnecting dashboard client must reconstruct the ordered transcript by combining a transcript synchronization
  with every transcript update published afterward, without missing or duplicating an entry - even when the
  synchronization races ongoing streamed publication, or races a concurrent burst of unrelated publications. These
  scenarios drive the published squad-hq process with the fake provider and observe only the real protocol through
  the headless UI client and the fake-provider control pipe; step definitions never inspect production transcript
  storage, pause publication, or read an internal announcement journal.

  Scenario: A transcript synchronization racing ongoing streamed publication reconstructs the full entry
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
    And the "coder" agent emits a final assistant message "Hello world"
    Then the reconciled transcript for role "coder" contains exactly these entries in order:
      | source    | content              |
      | user      | What should I build? |
      | assistant | Hello world          |
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero

  Scenario: A transcript synchronization racing a concurrent burst of publications loses and duplicates nothing
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    When the "coder" agent concurrently emits these system messages while a transcript synchronization races them:
      | content |
      | burst-1 |
      | burst-2 |
      | burst-3 |
      | burst-4 |
      | burst-5 |
    Then the reconciled transcript for role "coder" contains each of these entries exactly once:
      | source | content |
      | system | burst-1 |
      | system | burst-2 |
      | system | burst-3 |
      | system | burst-4 |
      | system | burst-5 |
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero
