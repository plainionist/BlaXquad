Feature: Interaction cancellation and transcript retention

  Aborting a role, that role's session failing, or a Headquarters-control shutdown cancels the role's pending
  interaction, and a response sent afterward is rejected with a protocol error without disturbing the interaction
  owner's session state. A pending permission also keeps its transcript context visible to a reconnecting
  dashboard even after enough later activity would otherwise evict it as the oldest unprotected entry, and the
  interaction remains answerable afterward.

  Background:

    Given `blaxquad/squad.json` configures:
      | role     |
      | coder    |
      | reviewer |

    When the operator launches Headquarters

    Then Headquarters starts an agent session for role "coder"
    And Headquarters starts an agent session for role "reviewer"

  Scenario: Abort cancels a role's pending interaction and a late response is rejected

    When the "coder" agent requests permission "permission-1" with description "Run the deploy script?"

    Then the dashboard shows a pending permission "permission-1" for role "coder" with description "Run the deploy script?"

    When the user aborts role "coder"

    Then the "coder" agent observes an abort

    When the user responds to permission "permission-1" for role "coder" with approved "true"

    Then the user observes a protocol error mentioning "permission-1"

  Scenario: A session failure cancels a role's pending interaction and a late response is rejected

    When the "coder" agent requests permission "permission-1" with description "Run the deploy script?"

    Then the dashboard shows a pending permission "permission-1" for role "coder" with description "Run the deploy script?"

    When the "coder" agent fails its session with message "provider connection lost"

    Then the dashboard shows role "coder" at status "error"

    When the user responds to permission "permission-1" for role "coder" with approved "true"

    Then the user observes a protocol error mentioning "permission-1"

  Scenario: A Headquarters-control shutdown completes while an interaction remains pending

    When the "reviewer" agent requests input "input-1" with prompt "Which branch should I use?" and freeform "true":
      | choice |

    Then the dashboard shows a pending input "input-1" for role "reviewer" with prompt "Which branch should I use?" and freeform "true":
      | choice |

    When the operator shuts down Headquarters

    Then Headquarters exits with code 0
    And the "reviewer" agent observes its pending interactions were cancelled

  Scenario: A recoverable response failure that races headquarters shutdown does not fault cleanup

    When the "coder" agent requests permission "permission-1" with description "Run the deploy script?"

    Then the dashboard shows a pending permission "permission-1" for role "coder" with description "Run the deploy script?"

    When the "coder" agent holds its next permission response pending
    And the user responds to permission "permission-1" for role "coder" with approved "true"

    Then the "coder" agent observes a permission response for "permission-1" approved "true"

    When the operator begins shutting down Headquarters without waiting for it to exit

    Then the "coder" agent observes its pending interactions were cancelled

    When the "coder" agent fails its pending permission response with message "provider connection lost"
    And Headquarters' process exits on its own

    Then Headquarters exits with code 0

  Scenario: A pending permission's transcript context survives crossing the live-retention boundary

    When the "coder" agent requests permission "permission-1" with description "Run the deploy script?"

    Then the dashboard shows a pending permission "permission-1" for role "coder" with description "Run the deploy script?"

    When the "coder" agent emits a system message with 250000 characters
    And the "coder" agent emits a system message with 250000 characters
    And the "coder" agent emits a system message with 250000 characters
    And the "coder" agent emits a system message with 250000 characters
    And the "coder" agent emits a system message with 250000 characters

    Then the dashboard shows a pending permission "permission-1" for role "coder" with description "Run the deploy script?"

    When the user requests a fresh transcript synchronization for role "coder"

    Then the freshly synchronized transcript for role "coder" contains an entry with source "harness" and content "Permission required: Run the deploy script?."

    When the user responds to permission "permission-1" for role "coder" with approved "true"

    Then the "coder" agent observes a permission response for "permission-1" approved "true"
