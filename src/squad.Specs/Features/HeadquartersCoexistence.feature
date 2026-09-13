Feature: Independent headquarters instances coexist

  Two headquarters instances launched for different projects must not interfere with each other. Stopping or
  terminating one instance's process must never disturb the other's running session.

  Scenario: Stopping one headless instance leaves an unrelated instance running

    Given the operator configures independent squad projects "first" and "second"

    When the operator launches Headquarters with the "stdio" UI transport for project "first"
    And the operator launches Headquarters with the "stdio" UI transport for project "second"
    And a UI-protocol client sends "ui.ready" to project "first"
    And a UI-protocol client sends "ui.ready" to project "second"

    Then a "transcript.update" message for role "coder" with content "Session started." is written to stdout for project "first"
    And a "transcript.update" message for role "coder" with content "Session started." is written to stdout for project "second"

    When the operator shuts down Headquarters for project "first"

    Then Headquarters exits with code 0 for project "first"
    And Headquarters for project "second" is still running

    When a UI-protocol client sends a "prompt.send" command for role "coder" with prompt "still-alive" to project "second"

    Then a "transcript.update" message for role "coder" with content "echo: still-alive" is written to stdout for project "second"
