Feature: Prompt isolation, serialization, and readiness

  Manual prompts sent through the real UI protocol reach only their addressed role, two prompts for the same role
  are delivered in order with no overlap, and role readiness follows real provider idle and busy transitions -
  observed only through "prompt.send", the fake-agent control pipe, and "squad-hq wait-for-agent", never through
  SquadViewModel, its role dictionaries, or its operation coordinator.

  NOTE: the "another role continues independently" half of this issue's serialization acceptance criterion is not
  yet covered here. The real stdio host reads and dispatches UI commands one at a time, awaiting each role's full
  prompt round trip before the next line is even read - so a second role's prompt cannot be observed to progress
  while a first role's prompt is still outstanding across this transport. This is a process-boundary behavior
  difference from the white-box ViewModel-level scenario it replaces; see issue 017 for the open follow-up.

  Background:
    Given a role-interaction scenario configured with roles "coder,reviewer"

  Scenario: Only the addressed role observes a sent prompt
    When the role-interaction scenario starts with both roles established
    And the role-interaction scenario sends the prompt "Design the schema" to role "coder"
    Then the "coder" agent has received the prompt "Design the schema"
    And the "reviewer" agent has observed no prompt
    When the "coder" agent answers with "Schema drafted"
    Then the role-interaction scenario observes the transcript for role "coder" containing "Schema drafted"
    And the "reviewer" agent has observed no prompt

  Scenario: A second prompt for a role waits for the first to go idle
    When the role-interaction scenario starts with both roles established
    And the role-interaction scenario sends the prompt "first" to role "coder"
    Then the "coder" agent has received the prompt "first"
    When the role-interaction scenario sends the prompt "second" to role "coder"
    Then the "coder" agent has only observed the prompt "first"
    When the "coder" agent answers with "first done"
    Then the role-interaction scenario observes the transcript for role "coder" containing "first done"
    And the "coder" agent has eventually received the prompt "second"
    When the "coder" agent answers with "second done"
    Then the role-interaction scenario observes the transcript for role "coder" containing "second done"

  Scenario: Readiness follows a role's idle and busy transitions through prompt dispatch
    When the role-interaction scenario starts with both roles established
    And the role-interaction scenario begins waiting for the "coder" agent to become ready
    Then the "coder" readiness wait remains pending
    When the "coder" agent reports idle
    Then the "coder" readiness wait succeeds
    When the role-interaction scenario sends the prompt "Investigate the bug" to role "coder"
    And the role-interaction scenario begins waiting for the "coder" agent to become ready
    Then the "coder" agent has received the prompt "Investigate the bug"
    And the "coder" readiness wait remains pending
    When the "coder" agent answers with "Found it"
    Then the "coder" readiness wait succeeds

  Scenario: Readiness waits remain role-specific while sessions establish independently
    When the role-interaction scenario starts without waiting for either role to establish
    And the role-interaction scenario begins waiting for the "reviewer" agent to become ready
    Then the "reviewer" readiness wait remains pending
    When the "coder" agent session establishes
    And the "coder" agent reports idle
    And the role-interaction scenario begins waiting for the "coder" agent to become ready
    Then the "coder" readiness wait succeeds
    And the "reviewer" readiness wait remains pending
    When the "reviewer" agent session establishes
    And the "reviewer" agent reports idle
    Then the "reviewer" readiness wait succeeds
