Feature: Transcript tool output aggregation

  A tool call's output arrives from the provider as a sequence of already-complete cumulative values - the
  provider, not squad-hq, is responsible for computing what each update's full content is. The transcript must
  aggregate every update for one tool call into a single entry, replacing that entry's content in place rather
  than duplicating it, for as long as the call keeps streaming further updates. These scenarios drive the
  published squad-hq process with the fake provider and observe only the real protocol through the headless UI
  client and the fake-provider control pipe.

  Scenario: Repeated tool output updates replace one transcript entry's content without duplicating it
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    When the "coder" agent starts tool call "X" named "powershell"
    And the "coder" agent emits tool output "LINE-1" for tool call "X"
    Then the backend scenario observes a transcript update for role "coder" with source "tool" and content "powershell\nLINE-1"
    When the "coder" agent emits tool output "LINE-1\nLINE-2" for tool call "X"
    Then the backend scenario observes a transcript update for role "coder" with source "tool" and content "powershell\nLINE-1\nLINE-2"
    And the most recently observed transcript updates for role "coder" report the same entry index
    When the "coder" agent emits tool output "LINE-1\nLINE-2\nLINE-3" for tool call "X"
    Then the backend scenario observes a transcript update for role "coder" with source "tool" and content "powershell\nLINE-1\nLINE-2\nLINE-3"
    And the most recently observed transcript updates for role "coder" report the same entry index
    When the "coder" agent completes tool call "X" named "powershell" with detailed output "Build succeeded"
    When the backend scenario requests a fresh transcript synchronization
    Then the transcript synchronization for role "coder" includes exactly these entries:
      | source | content                            |
      | tool   | powershell\nLINE-1\nLINE-2\nLINE-3 |
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero
