Feature: Transcript tool lifecycle state

  A running tool call is visible as the role's active tool only while it is running, progress reports never
  replace or corrupt the tool's aggregated output, a completion's own detailed output is used as display content
  only when no output ever streamed, and the active tool clears once the call completes. These scenarios drive the
  published squad-hq process with the fake provider and observe only the real protocol through the headless UI
  client and the fake-provider control pipe.

  Scenario: The active tool is visible only while the tool call is running
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    When the "coder" agent starts tool call "X" named "git status"
    Then the backend scenario observes role "coder" with active tool "git status"
    When the "coder" agent completes tool call "X" named "git status" with detailed output "clean"
    Then the backend scenario observes role "coder" with no active tool
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero

  Scenario: Progress reports remain separate from a tool call's aggregated output
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    When the "coder" agent starts tool call "X" named "powershell"
    And the "coder" agent reports progress "Waiting" for tool call "X"
    And the "coder" agent emits partial tool output "DONE" for tool call "X"
    And the "coder" agent reports progress "Finishing" for tool call "X"
    And the "coder" agent completes tool call "X" named "powershell" with detailed output "DONE\nmetadata"
    Then the reconciled transcript for role "coder" contains each of these entries exactly once:
      | source | content          |
      | tool   | powershell\nDONE |
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero

  Scenario: A completion's detailed output is a display fallback only when no output ever streamed
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    When the "coder" agent starts tool call "X" named "powershell"
    And the "coder" agent completes tool call "X" named "powershell" with detailed output "FINAL"
    Then the reconciled transcript for role "coder" contains each of these entries exactly once:
      | source | content           |
      | tool   | powershell\nFINAL |
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero
