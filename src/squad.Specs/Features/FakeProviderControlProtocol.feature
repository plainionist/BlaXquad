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

  Scenario: A reply to a role with no observed session fails fast with an explicit diagnostic
    Given a fake-provider control server is listening
    When the server replies to role "no-such-role" with content "hello"
    Then the server reports a protocol error mentioning "role"

  Scenario: A reply to a session that has since been disposed is rejected with an explicit diagnostic
    Given a fake-provider control server is listening
    And an authenticated fake-provider control client is connected
    When the client reports a session started and then disposed for role "coder" and session "session-1"
    And the server replies to role "coder" with content "hello"
    Then the server reports a protocol error mentioning "disposed"

  Scenario: A session that started but was never disposed is reported by the server's teardown diagnostic
    Given a fake-provider control server is listening
    And an authenticated fake-provider control client is connected
    When the client reports a session started for role "coder" and session "session-leaked"
    Then the server's undisposed-session diagnostic mentions role "coder" and session "session-leaked"

  Scenario: A session that started and was disposed is absent from the server's teardown diagnostic
    Given a fake-provider control server is listening
    And an authenticated fake-provider control client is connected
    When the client reports a session started and then disposed for role "coder" and session "session-1"
    Then the server's undisposed-session diagnostic is empty
