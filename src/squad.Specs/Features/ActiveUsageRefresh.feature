Feature: Active usage refresh reaches the ui before idle
  While a role's provider session is still working, newly available context and AIC usage reaches the real UI
  JSON protocol immediately, and the values observed once the role goes idle remain the latest reported. This
  proves the process/protocol boundary needed by the Copilot adapter's active usage refresh policy without
  depending on any real coding agent or its fixed five-second cadence, which is verified separately by a real
  Copilot smoke run.

  Background:
    Given a git project configured with a "coder" role using the controllable provider fixture

  Scenario: Usage reported while still working reaches the ui before idle, and idle preserves the latest usage
    When squad-hq is launched with "--ui stdio" using the controllable provider fixture
    And the ui completes the ready handshake
    And the controllable provider session for role "coder" has started
    And the ui sends prompt "hello" to role "coder"
    And the controllable provider reports context usage 100 of 1000 and AIC usage 0.25 for role "coder"
    Then a "state.snapshot" message reports role "coder" as working with context usage 100 of 1000 and AIC usage 0.25
    When the controllable provider goes idle for role "coder" with context usage 150 of 1000 and AIC usage 0.5
    Then a "state.snapshot" message reports role "coder" as idle with context usage 150 of 1000 and AIC usage 0.5
