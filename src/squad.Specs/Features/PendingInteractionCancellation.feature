Feature: Pending interaction cancellation

  A role's pending interaction is cancelled by that role's abort, its session failing, or a host-control shutdown -
  observed only through the real UI protocol and the fake-agent control pipe, never through SquadViewModel, its
  pending-interaction collections, or "Recording*" objects.

  Background:
    Given a backend scenario configured with roles "coder,reviewer"
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    And the backend scenario observes a session started for role "reviewer" across the control pipe

  Scenario: Abort cancels a role's pending interaction and a late response is rejected
    When the "coder" agent requests permission "permission-1" with description "Run the deploy script?"
    Then the backend scenario observes a pending permission "permission-1" for role "coder" with description "Run the deploy script?"
    When the backend scenario requests an abort for role "coder"
    Then the "coder" agent observes an abort
    When the backend scenario responds to permission "permission-1" for role "coder" with approved "true"
    Then the backend scenario observes a protocol error mentioning "permission-1"

  Scenario: A session failure cancels a role's pending interaction and a late response is rejected
    When the "coder" agent requests permission "permission-1" with description "Run the deploy script?"
    Then the backend scenario observes a pending permission "permission-1" for role "coder" with description "Run the deploy script?"
    When the "coder" agent fails its session with message "provider connection lost"
    Then the backend scenario observes role "coder" at status "error"
    When the backend scenario responds to permission "permission-1" for role "coder" with approved "true"
    Then the backend scenario observes a protocol error mentioning "permission-1"

  Scenario: A host-control shutdown completes while an interaction remains pending
    When the "reviewer" agent requests input "input-1" with prompt "Which branch should I use?"
    Then the backend scenario observes a pending input "input-1" for role "reviewer" with prompt "Which branch should I use?" and choices "" and freeform "true"
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero
    And the "reviewer" agent observes its pending interactions were cancelled
