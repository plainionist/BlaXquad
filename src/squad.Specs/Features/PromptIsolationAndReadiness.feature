Feature: Prompt isolation, serialization, and readiness

  Manual prompts sent through the real UI protocol reach only their addressed role, two prompts for the same role
  are delivered in order with no overlap, a second role's prompt is observed and can complete independently while
  a first role's prompt is still outstanding, and role readiness follows real provider idle and busy transitions -
  observed only through "prompt.send", the fake-agent control pipe, and "squad-hq wait-for-agent", never through
  SquadViewModel, its role dictionaries, or its operation coordinator.

  Background:
    Given `blaxquad/squad.json` configures:
      | role     |
      | coder    |
      | reviewer |

  Scenario: Only the addressed role observes a sent prompt
    When the operator launches Headquarters
    Then Headquarters starts an agent session for role "coder"
    And Headquarters starts an agent session for role "reviewer"
    When the backend scenario sends the prompt "Design the schema" to role "coder"
    Then the "coder" agent observes the prompt "Design the schema"
    And the "reviewer" agent has observed no prompt
    When the "coder" agent replies with "Schema drafted"
    Then the backend scenario observes the transcript for role "coder" containing "Schema drafted"
    And the "reviewer" agent has observed no prompt

  Scenario: A second prompt for a role waits for the first to go idle
    When the operator launches Headquarters
    Then Headquarters starts an agent session for role "coder"
    And Headquarters starts an agent session for role "reviewer"
    When the backend scenario sends the prompt "first" to role "coder"
    Then the "coder" agent observes the prompt "first"
    When the backend scenario sends the prompt "second" to role "coder"
    Then the "coder" agent has only observed the prompt "first"
    When the "coder" agent replies with "first done"
    Then the backend scenario observes the transcript for role "coder" containing "first done"
    And the "coder" agent observes the prompt "second"
    When the "coder" agent replies with "second done"
    Then the backend scenario observes the transcript for role "coder" containing "second done"

  Scenario: A prompt for another role is observed and answered while a first role's prompt is still outstanding
    When the operator launches Headquarters
    Then Headquarters starts an agent session for role "coder"
    And Headquarters starts an agent session for role "reviewer"
    When the backend scenario sends the prompt "first" to role "coder"
    Then the "coder" agent observes the prompt "first"
    When the backend scenario sends the prompt "for reviewer" to role "reviewer"
    Then the "reviewer" agent observes the prompt "for reviewer"
    When the "reviewer" agent replies with "reviewer done"
    Then the backend scenario observes the transcript for role "reviewer" containing "reviewer done"
    When the "coder" agent replies with "first done"
    Then the backend scenario observes the transcript for role "coder" containing "first done"

  Scenario: Readiness follows a role's idle and busy transitions through prompt dispatch
    When the operator launches Headquarters
    Then Headquarters starts an agent session for role "coder"
    And Headquarters starts an agent session for role "reviewer"
    When the operator begins waiting for role "coder" to become ready with `squad-hq wait-for-agent`
    Then role "coder"'s readiness wait remains pending
    When the "coder" agent emits idle
    Then role "coder"'s readiness wait succeeds
    When the backend scenario sends the prompt "Investigate the bug" to role "coder"
    And the operator begins waiting for role "coder" to become ready with `squad-hq wait-for-agent`
    Then the "coder" agent observes the prompt "Investigate the bug"
    And role "coder"'s readiness wait remains pending
    When the "coder" agent replies with "Found it"
    Then role "coder"'s readiness wait succeeds

  Scenario: Readiness waits remain role-specific while sessions establish independently
    When the operator launches Headquarters
    And the operator begins waiting for role "reviewer" to become ready with `squad-hq wait-for-agent`
    Then role "reviewer"'s readiness wait remains pending
    And Headquarters starts an agent session for role "coder"
    When the "coder" agent emits idle
    And the operator begins waiting for role "coder" to become ready with `squad-hq wait-for-agent`
    Then role "coder"'s readiness wait succeeds
    And role "reviewer"'s readiness wait remains pending
    And Headquarters starts an agent session for role "reviewer"
    When the "reviewer" agent emits idle
    Then role "reviewer"'s readiness wait succeeds
