Feature: Transcript ordinary tool command presentation

  A tool call's start arguments can carry a known shell command, an argument shape the projector does not
  recognize, or simply a tool name that happens to contain a word associated with reading. A known shell runner's
  command argument must be decoded into readable display text, an unrecognized argument shape must remain available
  in its raw form rather than being dropped, and a tool name alone must never misclassify an ordinary tool call as a
  file read. These scenarios drive the published squad-hq process with the fake provider and observe only the real
  protocol through the headless UI client and the fake-provider control pipe.

  Background:
    Given `blaxquad/squad.json` configures:
      | role  |
      | coder |
    When the operator launches Headquarters
    Then Headquarters starts an agent session for role "coder"

  Scenario: A known shell tool's command argument is decoded into its display text
    When the "coder" agent starts tool call "X" named "powershell" with arguments:
      """
      {"command":"Get-ChildItem -Path \u0022C:\\work\u0022","description":"List files"}
      """
    Then the dashboard receives a transcript update for role "coder" with source "tool"
    When the user requests a fresh transcript synchronization for role "coder"
    Then the transcript synchronization for role "coder" includes exactly these entries:
      | source | content                                    |
      | tool   | powershell Get-ChildItem -Path "C:\work"   |
    When the operator shuts down Headquarters
    Then Headquarters exits with code 0

  Scenario: An unrecognized tool argument shape remains available in its raw form
    When the "coder" agent starts tool call "X" named "glob" with arguments:
      """
      {"pattern":"**/*","paths":"C:\\work"}
      """
    Then the dashboard receives a transcript update for role "coder" with source "tool"
    When the user requests a fresh transcript synchronization for role "coder"
    # The data-table cell below needs two literal backslash characters ("\\") to represent each single actual
    # backslash in the raw JSON text production echoes back (via JsonElement.GetRawText()) - Reqnroll's own
    # data-table parsing unescapes "\\" to "\" before this step ever sees the cell, exactly like a C# string
    # literal needs "\\\\" to encode two real backslash characters. So "C:\\\\work" here decodes to the same
    # "C:\\work" (two real backslashes) that GetRawText() returns for the "C:\\work" argument passed above.
    Then the transcript synchronization for role "coder" includes exactly these entries:
      | source | content                                        |
      | tool   | glob {"pattern":"**/*","paths":"C:\\\\work"}   |
    When the operator shuts down Headquarters
    Then Headquarters exits with code 0

  Scenario: Tool names containing read-like words remain ordinary tool entries
    When the "coder" agent starts tool call "P" named "preview"
    And the "coder" agent emits tool output "preview output" for tool call "P"
    And the "coder" agent starts tool call "O" named "open_connection"
    And the "coder" agent emits tool output "connection output" for tool call "O"
    And the "coder" agent starts tool call "T" named "thread_status"
    And the "coder" agent emits tool output "thread output" for tool call "T"
    Then the dashboard receives a transcript update for role "coder" with source "tool" and content "preview\npreview output"
    And the dashboard receives a transcript update for role "coder" with source "tool" and content "open_connection\nconnection output"
    And the dashboard receives a transcript update for role "coder" with source "tool" and content "thread_status\nthread output"
    When the user requests a fresh transcript synchronization for role "coder"
    Then the transcript synchronization for role "coder" includes exactly these entries:
      | source | content                             |
      | tool   | preview\npreview output             |
      | tool   | open_connection\nconnection output  |
      | tool   | thread_status\nthread output        |
    When the operator shuts down Headquarters
    Then Headquarters exits with code 0
