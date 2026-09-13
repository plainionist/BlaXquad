Feature: Transcript concurrent tool-call correlation

  Two tool calls can be active at the same time, each streaming its own output. Every emitted tool output update
  must reach only its matching tool call's transcript entry and preserve that call's own emission order, even
  when updates for the two calls are interleaved. This scenario drives the published squad-hq process with the
  fake provider and observes only the real protocol through the headless UI client and the fake-provider control
  pipe.

  Background:

    Given `blaxquad/squad.json` configures:
      | role  |
      | coder |

    When the operator launches Headquarters

    Then Headquarters starts an agent session for role "coder"

  Scenario: Interleaved concurrent tool output remains correlated by tool call ID

    When the "coder" agent starts tool call "X" named "powershell X"
    And the "coder" agent starts tool call "Y" named "powershell Y"

    Then the dashboard receives a transcript update for role "coder" with source "tool" and content "powershell X"
    And the dashboard receives a transcript update for role "coder" with source "tool" and content "powershell Y"
    And the most recently received transcript updates for role "coder" report different entry indices

    When the "coder" agent emits tool output "X-1\n" for tool call "X"
    And the "coder" agent emits tool output "Y-1\n" for tool call "Y"
    And the "coder" agent emits tool output "X-1\nX-2\n" for tool call "X"
    And the "coder" agent emits tool output "Y-1\nY-2\n" for tool call "Y"

    Then the dashboard receives a transcript update for role "coder" with source "tool" and content "powershell X\nX-1\nX-2\n"
    And the dashboard receives a transcript update for role "coder" with source "tool" and content "powershell Y\nY-1\nY-2\n"
    And the most recently received transcript updates for role "coder" report different entry indices

    When the user requests a fresh transcript synchronization for role "coder"

    Then the transcript synchronization for role "coder" includes exactly these entries:
      | source | content                    |
      | tool   | powershell X\nX-1\nX-2\n |
      | tool   | powershell Y\nY-1\nY-2\n |

    When the operator shuts down Headquarters

    Then Headquarters exits with code 0
