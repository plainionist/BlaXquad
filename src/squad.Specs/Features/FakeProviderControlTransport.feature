Feature: Private fake-provider control transport

  A private, uniquely named local pipe lets a process specification observe the fake provider's real session
  lifecycle - start and disposal - from inside the separately launched, provider-free squad-hq process, using
  typed, versioned, newline-delimited JSON envelopes correlated by id. squad-hq itself never parses, forwards, or
  otherwise knows about this channel; its pipe name and one-time authentication token reach the provider process
  only through environment variables the fake provider itself reads.

  Scenario: A process specification observes session start and disposal across the pipe
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes a session disposed for role "coder" across the control pipe
    And the backend scenario observes an exit code of zero
