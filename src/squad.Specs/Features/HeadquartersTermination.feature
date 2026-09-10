Feature: Headquarters termination on UI closure and caller cancellation

  A real multi-role squad-hq process terminates cleanly - releasing every externally observable resource and
  permitting a fresh, healthy launch against the same project afterward - both when its headless UI's standard
  input closes (whether after or before the "ui.ready" handshake) and when the platform's own normal cancellation
  signal reaches the launched process, proven only through the real process, the real UI protocol, the real
  "squad-hq wait-for-agent" host-control command, and the fake-provider control pipe - never through
  SquadApplication, a recording window test double, or an injected caller cancellation token.

  Scenario: Closing standard input after readiness terminates the process cleanly
    Given `blaxquad/squad.json` configures:
      | role     |
      | coder    |
      | reviewer |
    When the operator launches Headquarters
    Then Headquarters starts an agent session for role "coder"
    And Headquarters starts an agent session for role "reviewer"
    When the operator closes Headquarters' standard input
    Then Headquarters exits with code 0
    And Headquarters disposes the agent session for role "coder"
    And Headquarters disposes the agent session for role "reviewer"
    And the operator finds Headquarters unavailable for role "coder"
    When the operator launches a new Headquarters against the same project
    Then the new Headquarters process reports ready

  Scenario: Closing standard input before the ready handshake still terminates the process cleanly
    Given `blaxquad/squad.json` configures:
      | role  |
      | coder |
    When the operator launches a squad host without completing the ready handshake
    And the operator closes Headquarters' standard input
    Then Headquarters exits with code 0
    And the operator finds Headquarters unavailable for role "coder"
    When the operator launches a new Headquarters against the same project
    Then the new Headquarters process reports ready

  Scenario: The platform's cancellation signal after readiness terminates the process cleanly
    Given `blaxquad/squad.json` configures:
      | role     |
      | coder    |
      | reviewer |
    When the operator launches a cancellable Headquarters
    Then Headquarters starts an agent session for role "coder"
    And Headquarters starts an agent session for role "reviewer"
    When the platform delivers its cancellation signal to Headquarters
    Then Headquarters exits with code 0
    And Headquarters disposes the agent session for role "coder"
    And Headquarters disposes the agent session for role "reviewer"
    And the operator finds Headquarters unavailable for role "coder"
    When the operator launches a new Headquarters against the same project
    Then the new Headquarters process reports ready
