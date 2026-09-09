Feature: Transcript concurrent tool-call correlation

  Two tool calls can be active at the same time, each streaming its own output. Every emitted partial output
  fragment must reach only its matching tool call's transcript entry and preserve that call's own emission order,
  even when fragments for the two calls are interleaved. This scenario drives the published squad-hq process with
  the fake provider and observes only the real protocol through the headless UI client and the fake-provider
  control pipe.

  Scenario: Interleaved concurrent tool output remains correlated by tool call ID
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    When the "coder" agent starts tool call "X" named "powershell X"
    And the "coder" agent starts tool call "Y" named "powershell Y"
    Then the backend scenario observes a transcript update for role "coder" with source "tool" and content "powershell X"
    And the backend scenario observes a transcript update for role "coder" with source "tool" and content "powershell Y"
    And the most recently observed transcript updates for role "coder" report different entry indices
    When the "coder" agent emits partial tool output "X-1\n" for tool call "X"
    And the "coder" agent emits partial tool output "Y-1\n" for tool call "Y"
    And the "coder" agent emits partial tool output "X-2\n" for tool call "X"
    And the "coder" agent emits partial tool output "Y-2\n" for tool call "Y"
    Then the backend scenario observes a transcript update for role "coder" with source "tool" and content "powershell X\nX-1\nX-2\n"
    And the backend scenario observes a transcript update for role "coder" with source "tool" and content "powershell Y\nY-1\nY-2\n"
    And the most recently observed transcript updates for role "coder" report different entry indices
    When the backend scenario requests a fresh transcript synchronization
    Then the transcript synchronization for role "coder" includes exactly these entries:
      | source | content                    |
      | tool   | powershell X\nX-1\nX-2\n |
      | tool   | powershell Y\nY-1\nY-2\n |
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero
