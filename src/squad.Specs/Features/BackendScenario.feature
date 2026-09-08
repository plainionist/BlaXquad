Feature: Backend scenario lifecycle

  BackendScenario is the single test-owned composition root for one backend-process specification: it configures
  the role, starts the published, provider-free squad-hq under "--ui stdio" with an explicit test provider,
  observes readiness through the real "ui.ready" handshake, and requests a normal host-controlled shutdown -
  all without step definitions touching workspace paths, process handles, protocol DTOs, or product objects.

  Scenario: A backend process starts, becomes ready, and shuts down cleanly through BackendScenario
    Given a backend scenario configured with a "coder" role
    When the backend scenario starts squad-hq with the echo provider fixture
    Then the backend scenario reports the process as ready
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero
