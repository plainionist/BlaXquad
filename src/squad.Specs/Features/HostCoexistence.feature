Feature: Independent headquarters instances coexist
  Two headquarters instances launched for different projects must not interfere with each other. Stopping or
  terminating one instance's process must never disturb the other's running session.

  Scenario: Stopping one headless instance leaves an unrelated instance running
    Given independent squad projects "first" and "second" using the echo provider fixture
    When squad-hq is launched with "--ui stdio" for project "first"
    And squad-hq is launched with "--ui stdio" for project "second"
    And the ui sends "ui.ready" to project "first"
    And the ui sends "ui.ready" to project "second"
    Then a "transcript.update" message for role "coder" with content "Session started." is written to stdout for project "first"
    And a "transcript.update" message for role "coder" with content "Session started." is written to stdout for project "second"
    When squad-hq requests shutdown for project "first"
    Then the shutdown request succeeds for project "first"
    And the squad-hq process for project "first" exits with code "0"
    And the squad-hq process for project "second" is still running
    When the ui sends a "prompt.send" command for role "coder" with prompt "still-alive" to project "second"
    Then a "transcript.update" message for role "coder" with content "echo: still-alive" is written to stdout for project "second"
