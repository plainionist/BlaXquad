Feature: Fake-provider control transport protocol validation and concurrency

  The fake-provider control transport validates every incoming envelope before acting on it, and its single
  dispatch loop lets many concurrent session observations and their acknowledgements proceed without competing to
  read the pipe.

  Scenario: An invalid authentication token is rejected with an explicit diagnostic
    Given a fake-provider control server is listening
    When a client connects to the control pipe with an invalid token
    Then the client observes a protocol error mentioning "token"

  Scenario: An unsupported protocol version is rejected with an explicit diagnostic
    Given a fake-provider control server is listening
    When a client sends a "connect" envelope with protocol version 99
    Then the client observes a protocol error mentioning "version"

  Scenario: A missing correlation id is rejected with an explicit diagnostic
    Given a fake-provider control server is listening
    When a client sends a "connect" envelope with no correlation id
    Then the client observes a protocol error mentioning "correlation"

  Scenario: An unknown command is rejected with an explicit diagnostic
    Given a fake-provider control server is listening
    When a client sends an envelope with the unknown command "bogus-command"
    Then the client observes a protocol error mentioning "Unknown command"

  Scenario: Concurrent session observations and acknowledgements do not compete for reads
    Given a fake-provider control server is listening
    And an authenticated fake-provider control client is connected
    When the client concurrently reports 20 session starts and disposals
    Then the server observes all 20 session starts and disposals
