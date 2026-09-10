Feature: Stdio UI protocol over the real published process
  squad-hq launched with "--ui stdio" drives the real UiProtocolSession over the real published process's
  standard input and standard output. The shared fake-provider fixture completes launch far enough to exercise
  the protocol end to end without ever opening a native window.

  Background:
    Given a git project prepared with a "coder" role using the fake provider fixture

  Scenario: Startup waits for the ui.ready handshake before publishing state
    When squad-hq is launched with "--ui stdio"
    Then no protocol message is written to stdout yet
    When the ui sends "ui.ready"
    Then an initial "transcript.synchronize" message for role "coder" is written to stdout
    And a "state.snapshot" message is written to stdout

  Scenario: A prompt command produces a transcript update
    When squad-hq is launched with "--ui stdio"
    And the ui sends "ui.ready"
    Then a "transcript.update" message for role "coder" with content "Session started." is written to stdout
    When the ui sends a "prompt.send" command for role "coder" with prompt "hello"
    Then a "transcript.update" message for role "coder" with content "echo: hello" is written to stdout

  Scenario: The ui can request a transcript page and a recovery synchronization
    When squad-hq is launched with "--ui stdio"
    And the ui sends "ui.ready"
    Then a "transcript.update" message for role "coder" with content "Session started." is written to stdout
    When the ui sends a "prompt.send" command for role "coder" with prompt "hello"
    Then a "transcript.update" message for role "coder" with content "echo: hello" is written to stdout
    When the ui requests a transcript page for role "coder" before index 1
    Then a "transcript.page" message for role "coder" is written to stdout
    When the ui requests transcript synchronization
    Then a recovery "transcript.synchronize" message for role "coder" is written to stdout

  Scenario: A host-controlled shutdown closes the process without closing standard input
    When squad-hq is launched with "--ui stdio"
    And the ui sends "ui.ready"
    Then a "transcript.update" message for role "coder" with content "Session started." is written to stdout
    When squad-hq requests shutdown for the workspace
    Then the shutdown request succeeds
    And the squad-hq process exits with code "0"

  Scenario: Protocol output and process diagnostics are kept separate
    When squad-hq is launched with "--ui stdio"
    And the ui sends "ui.ready"
    Then a "transcript.update" message for role "coder" with content "Session started." is written to stdout
    When the ui sends a "prompt.send" command for role "coder" with prompt "hello"
    Then every stdout line is a well-formed protocol envelope
    And standard error contains no protocol envelope
