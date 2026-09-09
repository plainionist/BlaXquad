Feature: Headquarters termination on UI closure and caller cancellation

  A real multi-role squad-hq process terminates cleanly - releasing every externally observable resource and
  permitting a fresh, healthy launch against the same project afterward - both when its headless UI's standard
  input closes (whether after or before the "ui.ready" handshake) and when the platform's own normal cancellation
  signal reaches the launched process, proven only through the real process, the real UI protocol, the real
  "squad-hq wait-for-agent" host-control command, and the fake-provider control pipe - never through
  SquadApplication, a recording window test double, or an injected caller cancellation token.

  Scenario: Closing standard input after readiness terminates the process cleanly
    Given a backend scenario configured with roles "coder,reviewer"
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    And the backend scenario observes a session started for role "reviewer" across the control pipe
    When the backend scenario closes its standard input
    Then the backend scenario observes an exit code of zero
    And the backend scenario observes a session disposed for role "coder" across the control pipe
    And the backend scenario observes a session disposed for role "reviewer" across the control pipe
    And the backend scenario confirms host control is unavailable for role "coder"
    When a fresh backend scenario starts squad-hq against the same workspace with the echo provider fixture
    Then the fresh backend scenario reports the process as ready

  Scenario: Closing standard input before the ready handshake still terminates the process cleanly
    Given a backend scenario configured with a "coder" role
    When the backend scenario launches squad-hq without completing the ready handshake
    And the backend scenario closes its standard input
    Then the backend scenario observes an exit code of zero
    And the backend scenario confirms host control is unavailable for role "coder"
    When a fresh backend scenario starts squad-hq against the same workspace with the echo provider fixture
    Then the fresh backend scenario reports the process as ready

  Scenario: The platform's cancellation signal after readiness terminates the process cleanly
    Given a backend scenario configured with roles "coder,reviewer"
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts a cancellable squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    And the backend scenario observes a session started for role "reviewer" across the control pipe
    When the backend scenario delivers the platform's cancellation signal
    Then the backend scenario observes an exit code of zero
    And the backend scenario observes a session disposed for role "coder" across the control pipe
    And the backend scenario observes a session disposed for role "reviewer" across the control pipe
    And the backend scenario confirms host control is unavailable for role "coder"
    When a fresh backend scenario starts squad-hq against the same workspace with the echo provider fixture
    Then the fresh backend scenario reports the process as ready
