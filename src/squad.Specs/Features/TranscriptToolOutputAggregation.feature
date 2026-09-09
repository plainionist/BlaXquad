Feature: Transcript tool output aggregation

  A tool call's output can arrive from the real Copilot SDK as a growing cumulative snapshot, as independent
  incremental fragments, or as a snapshot later rewritten to content that no longer extends the prior one - and the
  transcript must always aggregate every one of these into a single entry: growing snapshots replace rather than
  duplicate, independent fragments concatenate, and a rewritten snapshot replaces superseded output rather than
  appending to it. A completion's own detailed output is used only as a fallback when no output ever streamed.
  These scenarios drive the published squad-hq process with the fake provider and observe only the real protocol
  through the headless UI client and the fake-provider control pipe - the fake provider applies raw partial output
  through the same real production <c>CopilotToolOutputNormalizer</c> the live Copilot SDK provider uses, so these
  scenarios prove real aggregation semantics rather than a pre-normalized test value.

  Scenario: Cumulative tool snapshots update one transcript entry without duplicating repeated content
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    When the "coder" agent starts tool call "X" named "powershell"
    And the "coder" agent emits partial tool output "LINE-1" for tool call "X"
    Then the backend scenario observes a transcript update for role "coder" with source "tool" and content "powershell\nLINE-1"
    When the "coder" agent emits partial tool output "LINE-1\nLINE-2" for tool call "X"
    Then the backend scenario observes a transcript update for role "coder" with source "tool" and content "powershell\nLINE-1\nLINE-2"
    And the most recently observed transcript updates for role "coder" report the same entry index
    When the "coder" agent emits partial tool output "LINE-1\nLINE-2" for tool call "X"
    And the "coder" agent emits partial tool output "LINE-1\nLINE-2\nLINE-3" for tool call "X"
    Then the backend scenario observes a transcript update for role "coder" with source "tool" and content "powershell\nLINE-1\nLINE-2\nLINE-3"
    And the most recently observed transcript updates for role "coder" report the same entry index
    When the "coder" agent completes tool call "X" named "powershell" with detailed output "Build succeeded"
    When the backend scenario requests a fresh transcript synchronization
    Then the transcript synchronization for role "coder" includes exactly these entries:
      | source | content                            |
      | tool   | powershell\nLINE-1\nLINE-2\nLINE-3 |
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero

  Scenario: Incremental tool output fragments concatenate into one transcript entry
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    When the "coder" agent starts tool call "X" named "powershell"
    And the "coder" agent emits partial tool output "LINE-1\n" for tool call "X"
    Then the backend scenario observes a transcript update for role "coder" with source "tool" and content "powershell\nLINE-1\n"
    When the "coder" agent emits partial tool output "LINE-2\n" for tool call "X"
    Then the backend scenario observes a transcript update for role "coder" with source "tool" and content "powershell\nLINE-1\nLINE-2\n"
    And the most recently observed transcript updates for role "coder" report the same entry index
    When the "coder" agent emits partial tool output "LINE-3\n" for tool call "X"
    Then the backend scenario observes a transcript update for role "coder" with source "tool" and content "powershell\nLINE-1\nLINE-2\nLINE-3\n"
    And the most recently observed transcript updates for role "coder" report the same entry index
    When the backend scenario requests a fresh transcript synchronization
    Then the transcript synchronization for role "coder" includes exactly these entries:
      | source | content                             |
      | tool   | powershell\nLINE-1\nLINE-2\nLINE-3\n |
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero

  Scenario: A rewritten snapshot replaces superseded output rather than appending
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    When the "coder" agent starts tool call "X" named "powershell"
    And the "coder" agent emits partial tool output "Progress 10" for tool call "X"
    Then the backend scenario observes a transcript update for role "coder" with source "tool" and content "powershell\nProgress 10"
    When the "coder" agent emits partial tool output "Progress 10\n" for tool call "X"
    Then the backend scenario observes a transcript update for role "coder" with source "tool" and content "powershell\nProgress 10\n"
    And the most recently observed transcript updates for role "coder" report the same entry index
    When the "coder" agent emits partial tool output "Progress 20" for tool call "X"
    Then the backend scenario observes a transcript update for role "coder" with source "tool" and content "powershell\nProgress 20"
    And the most recently observed transcript updates for role "coder" report the same entry index
    When the backend scenario requests a fresh transcript synchronization
    Then the transcript synchronization for role "coder" includes exactly these entries:
      | source | content               |
      | tool   | powershell\nProgress 20 |
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero

  Scenario: Streamed tool output is preserved and a completion's detailed output is not used as a fallback
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    When the "coder" agent starts tool call "X" named "run_in_terminal"
    And the "coder" agent emits partial tool output "3 files found" for tool call "X"
    Then the backend scenario observes a transcript update for role "coder" with source "tool" and content "run_in_terminal\n3 files found"
    When the "coder" agent completes tool call "X" named "run_in_terminal" with detailed output "Build succeeded"
    And the backend scenario requests a fresh transcript synchronization
    Then the transcript synchronization for role "coder" includes exactly these entries:
      | source | content                       |
      | tool   | run_in_terminal\n3 files found |
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero
