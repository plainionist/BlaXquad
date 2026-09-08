Feature: Minimal fake provider through the production SPI

  A minimal fake IAgentProviderFactory, loaded into the published, provider-free squad-hq by the same explicit
  "--provider" descriptor production providers use, establishes and disposes a configured role session through
  the normal provider/runtime/session lifecycle - proving the production SPI can host a fake without any product
  test hook, extra test assembly, or squad.CopilotSdk dependency.

  Scenario: The provider-free headquarters loads the fake provider and completes its session lifecycle
    Given a backend scenario configured with a "coder" role
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario reports the process as ready
    And the backend scenario observes role "coder" at status "running"
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero
