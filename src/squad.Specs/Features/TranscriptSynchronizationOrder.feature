Feature: Transcript synchronization order

  A reconnecting dashboard client must reconstruct the ordered transcript by combining a transcript synchronization
  with every transcript update published afterward, without missing or duplicating an entry - even when the
  synchronization races ongoing streamed publication, or races a concurrent burst of unrelated publications.

  Background:
    Given `blaxquad/squad.json` configures:
      | role  |
      | coder |
    When the operator launches Headquarters
    Then Headquarters starts an agent session for role "coder"

  Scenario: A transcript synchronization racing ongoing streamed publication reconstructs the full entry
    When the user sends "What should I build?" to role "coder"
    Then the "coder" agent observes the prompt "What should I build?"
    And the dashboard receives a transcript update for role "coder" with source "user" and content "What should I build?"
    When the "coder" agent emits an assistant delta "Hello "
    Then the dashboard receives a transcript update for role "coder" with source "assistant" and content "Hello "
    When the user requests a fresh transcript synchronization for role "coder"
    Then the transcript synchronization for role "coder" includes an entry with source "assistant" and content "Hello "
    When the "coder" agent emits an assistant delta "world"
    And the "coder" agent emits a final assistant message "Hello world"
    Then the reconciled transcript for role "coder" contains exactly these entries in order:
      | source    | content              |
      | user      | What should I build? |
      | assistant | Hello world          |
    When the operator shuts down Headquarters
    Then Headquarters exits with code 0

  Scenario: A transcript synchronization racing a concurrent burst of publications loses and duplicates nothing
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
    When the operator shuts down Headquarters
    Then Headquarters exits with code 0
