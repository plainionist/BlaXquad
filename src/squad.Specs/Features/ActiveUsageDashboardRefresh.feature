Feature: Active usage refresh reaches the dashboard before idle

  While a role's provider session is still working, newly available context and AI-credit usage reaches the
  dashboard immediately, and the values observed once the role goes idle remain the latest reported - proving the
  process/protocol boundary needed by the Copilot adapter's active usage refresh policy without depending on any
  real coding agent or its fixed five-second cadence, which is verified separately by a real Copilot smoke run.

  Background:

    Given `blaxquad/squad.json` configures:
      | role  |
      | coder |

    When the operator launches Headquarters

    Then Headquarters starts an agent session for role "coder"

  Scenario: Usage reported while still working reaches the dashboard before idle, and idle preserves the latest usage

    When the user sends "hello" to role "coder"

    Then the "coder" agent observes the prompt "hello"

    When the "coder" agent reports context usage 100 of 1000 and AIC usage 0.25

    Then the dashboard shows role "coder" as working with context usage 100 of 1000 and AIC usage 0.25

    When the "coder" agent goes idle with context usage 150 of 1000 and AIC usage 0.5

    Then the dashboard shows role "coder" as idle with context usage 150 of 1000 and AIC usage 0.5

  Scenario: A stale AIC usage checkpoint cannot overwrite a newer one while working or after idle

    When the user sends "hello" to role "coder"

    Then the "coder" agent observes the prompt "hello"

    When the "coder" agent reports context usage 100 of 1000 and AIC usage 3.5

    Then the dashboard shows role "coder" as working with context usage 100 of 1000 and AIC usage 3.5

    When the "coder" agent reports context usage 150 of 1000 and AIC usage 0

    Then the dashboard shows role "coder" as working with context usage 150 of 1000 and AIC usage 3.5
    And the dashboard does not show role "coder" at AIC usage 0 within 2 seconds

    When the "coder" agent goes idle with context usage 200 of 1000 and AIC usage 0

    Then the dashboard shows role "coder" as idle with context usage 200 of 1000 and AIC usage 3.5
    And the dashboard does not show role "coder" at AIC usage 0 within 2 seconds
