Feature: Transcript tool lifecycle state

  A running tool call is visible as the role's active tool only while it is running, progress reports never
  replace or corrupt the tool's aggregated output, a completion's own detailed output is used as display content
  only when no output ever streamed, and the active tool clears once the call completes. These scenarios drive the
  published squad-hq process with the fake provider and observe only the real protocol through the headless UI
  client and the fake-provider control pipe.

  Background:
    Given `blaxquad/squad.json` configures:
      | role  |
      | coder |
    When the operator launches Headquarters
    Then Headquarters starts an agent session for role "coder"

  Scenario: The active tool is visible only while the tool call is running
    When the "coder" agent starts tool call "X" named "git status"
    Then the dashboard shows role "coder" with active tool "git status"
    When the "coder" agent completes tool call "X" named "git status" with detailed output "clean"
    Then the dashboard shows role "coder" with no active tool
    When the operator shuts down Headquarters
    Then Headquarters exits with code 0

  Scenario: Progress reports remain separate from a tool call's aggregated output
    When the "coder" agent starts tool call "X" named "powershell"
    And the "coder" agent reports progress "Waiting" for tool call "X"
    And the "coder" agent emits tool output "DONE" for tool call "X"
    And the "coder" agent reports progress "Finishing" for tool call "X"
    And the "coder" agent completes tool call "X" named "powershell" with detailed output "DONE\nmetadata"
    Then the reconciled transcript for role "coder" contains each of these entries exactly once:
      | source | content          |
      | tool   | powershell\nDONE |
    When the operator shuts down Headquarters
    Then Headquarters exits with code 0

  Scenario: A completion's detailed output is a display fallback only when no output ever streamed
    When the "coder" agent starts tool call "X" named "powershell"
    And the "coder" agent completes tool call "X" named "powershell" with detailed output "FINAL"
    Then the reconciled transcript for role "coder" contains each of these entries exactly once:
      | source | content           |
      | tool   | powershell\nFINAL |
    When the operator shuts down Headquarters
    Then Headquarters exits with code 0
