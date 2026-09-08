Feature: Semantic headless UI client
  A reusable, test-owned client for one launched "squad-hq --ui stdio" process owns standard input, drains standard
  output and standard error concurrently, and privately frames the newline-delimited versioned UI protocol. Step
  definitions use only its semantic readiness, prompt-sending, role-status, transcript, and protocol-error
  operations against a real published squad-hq process - never raw JSON envelopes, streams, or the child process
  itself.

  Background:
    Given a git project configured with a "coder" role using the echo provider fixture
    And the published squad-hq is launched with "--ui stdio"

  Scenario: The client completes the real ui.ready handshake
    When the ui client completes the ready handshake
    Then the ui client observes role "coder" at status "running"

  Scenario: A prompt sent through the client reaches the real transcript
    Given the ui client has completed the ready handshake
    When the ui client sends prompt "hello" to role "coder"
    Then the ui client observes transcript content "echo: hello" for role "coder"
    And the ui client observes role "coder" at status "idle"

  Scenario: An unknown role reached through prompt sending is reported as a protocol error
    Given the ui client has completed the ready handshake
    When the ui client sends prompt "hello" to role "not-a-configured-role"
    Then the ui client reports a protocol error

  Scenario: A bounded role-status wait reports captured diagnostics on timeout
    Given the ui client has completed the ready handshake
    Then waiting up to 1 second for role "coder" to report status "not-a-real-status" times out with captured output
