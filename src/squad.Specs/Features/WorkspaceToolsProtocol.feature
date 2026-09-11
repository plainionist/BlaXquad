Feature: Workspace tools protocol

  squad-hq lets the operator configure narrow, workspace-scoped external tools (today the optional Git history
  viewer) it launches on the operator's behalf. Their availability is published exactly once during the "ui.ready"
  handshake as a "workspace-tools.snapshot" message, and each configured tool exposes its own dedicated, payload-free
  command to invoke it - driven here through the real, separately launched stdio-hosted process and the real
  headless UI client. Every step here is generic to "a configured tool's own field, flag, and command name" so a
  future tool proves itself with its own literals in a new scenario, never a bespoke step of its own.

  Background:
    Given `blaxquad/squad.json` configures:
      | role  |
      | coder |

  Scenario: An unconfigured tool reports its capability unavailable
    When the operator launches Headquarters with the "stdio" UI transport
    And a UI-protocol client sends "ui.ready"
    Then the workspace tools snapshot reports "gitHistoryAvailable" as unavailable

  Scenario: A configured tool whose executable cannot be resolved reports its capability unavailable
    Given the project configuration declares tool command "gitHistoryCommand" as:
      | argument                      |
      | not-a-real-executable-xyz.exe |
    When the operator launches Headquarters with the "stdio" UI transport
    And a UI-protocol client sends "ui.ready"
    Then the workspace tools snapshot reports "gitHistoryAvailable" as unavailable

  Scenario: A configured tool whose executable resolves reports its capability available and starts when invoked
    Given the project configuration declares tool command "gitHistoryCommand" as:
      | argument                              |
      | cmd.exe                               |
      | /c                                     |
      | echo opened > git-history-invoked.txt |
    When the operator launches Headquarters with the "stdio" UI transport
    And a UI-protocol client sends "ui.ready"
    Then the workspace tools snapshot reports "gitHistoryAvailable" as available
    When a UI-protocol client sends a "git-history.open" command with request id "req-open"
    Then file "git-history-invoked.txt" appears in the project root

  Scenario: Invoking an unavailable tool reports a correlated protocol error instead of silently doing nothing
    When the operator launches Headquarters with the "stdio" UI transport
    And a UI-protocol client sends "ui.ready"
    When a UI-protocol client sends a "git-history.open" command with request id "req-open"
    Then a correlated protocol error for request id "req-open" is reported

  Scenario: A malformed configured tool command fails launch with the normal configuration diagnostic
    Given the project configuration declares tool command "gitHistoryCommand" as an empty list
    When the operator launches Headquarters without completing the ready handshake
    And Headquarters' process exits on its own
    Then Headquarters exits with a non-zero code
    And Headquarters' standard error contains "gitHistoryCommand"
