Feature: Interaction publication and ownership

  Permission, input, and elicitation requests are published through the dashboard with every supported field and
  also appear in the transcript. A response reaches only its addressed role and request; wrong-role, duplicate,
  and late responses are rejected with a protocol error, and identical request IDs pending for two different roles
  remain independent.

  Background:
    Given `blaxquad/squad.json` configures:
      | role     |
      | coder    |
      | reviewer |
    When the operator launches Headquarters
    Then Headquarters starts an agent session for role "coder"
    And Headquarters starts an agent session for role "reviewer"

  Scenario: Published UI state contains every supported field for permission, input, and elicitation requests
    When the "coder" agent requests permission "permission-1" with description "Run the deploy script?"
    Then the dashboard shows a pending permission "permission-1" for role "coder" with description "Run the deploy script?"
    And the transcript for role "coder" contains "Permission required: Run the deploy script?."
    When the "coder" agent requests input "input-1" with prompt "Which branch should I use?" and freeform "false":
      | choice  |
      | main    |
      | develop |
    Then the dashboard shows a pending input "input-1" for role "coder" with prompt "Which branch should I use?" and freeform "false":
      | choice  |
      | main    |
      | develop |
    And the transcript for role "coder" contains "Which branch should I use?"
    When the "coder" agent requests elicitation "elicitation-1" with prompt "Confirm the deployment?":
      | mode | url |
      | form |     |
    Then the dashboard shows a pending elicitation "elicitation-1" for role "coder" with prompt "Confirm the deployment?":
      | mode | url |
      | form |     |
    And the transcript for role "coder" contains "Confirm the deployment?"
    When the "coder" agent requests elicitation "elicitation-2" with prompt "Complete sign-in":
      | mode | url                            |
      | url  | https://example.test/authorize |
    Then the dashboard shows a pending elicitation "elicitation-2" for role "coder" with prompt "Complete sign-in":
      | mode | url                            |
      | url  | https://example.test/authorize |

  Scenario: A successful response reaches only the addressed role
    When the "coder" agent requests permission "permission-1" with description "Run the deploy script?"
    Then the dashboard shows a pending permission "permission-1" for role "coder" with description "Run the deploy script?"
    When the user responds to permission "permission-1" for role "coder" with approved "true"
    Then the "coder" agent observes a permission response for "permission-1" approved "true"
    And the "reviewer" agent has not observed a permission response
    When the "reviewer" agent requests input "input-1" with prompt "Which branch should I use?" and freeform "true":
      | choice |
    Then the dashboard shows a pending input "input-1" for role "reviewer" with prompt "Which branch should I use?" and freeform "true":
      | choice |
    When the user responds to input "input-1" for role "reviewer" with answer "main"
    Then the "reviewer" agent observes an input response for "input-1" with answer "main"
    And the "coder" agent has not observed an input response
    When the "coder" agent requests elicitation "elicitation-1" with prompt "Confirm the deployment?":
      | mode | url |
      | form |     |
    Then the dashboard shows a pending elicitation "elicitation-1" for role "coder" with prompt "Confirm the deployment?":
      | mode | url |
      | form |     |
    When the user responds to elicitation "elicitation-1" for role "coder" with action "accept":
      | form value |
      | yes        |
    Then the "coder" agent observes an elicitation response for "elicitation-1" with action "accept":
      | form value |
      | yes        |
    And the "reviewer" agent has not observed an elicitation response

  Scenario: Wrong-role, duplicate, and late responses are rejected without disturbing the owner's pending request
    When the "coder" agent requests permission "permission-1" with description "Run the deploy script?"
    Then the dashboard shows a pending permission "permission-1" for role "coder" with description "Run the deploy script?"
    When the user responds to permission "permission-1" for role "reviewer" with approved "true"
    Then the user observes a protocol error mentioning "permission-1"
    And the dashboard shows a pending permission "permission-1" for role "coder" with description "Run the deploy script?"
    When the user responds to permission "permission-1" for role "coder" with approved "true"
    Then the "coder" agent observes a permission response for "permission-1" approved "true"
    When the user responds to permission "permission-1" for role "coder" with approved "true"
    Then the user observes a protocol error mentioning "permission-1"

  Scenario: An accepted fixed-choice input response is published once as a user transcript entry, ordered before later output, and survives synchronization
    When the "coder" agent requests input "input-1" with prompt "Which branch should I use?" and freeform "false":
      | choice  |
      | main    |
      | develop |
    Then the dashboard shows a pending input "input-1" for role "coder" with prompt "Which branch should I use?" and freeform "false":
      | choice  |
      | main    |
      | develop |
    When the user responds to input "input-1" for role "reviewer" with answer "wrong-role" and freeform "false"
    Then the user observes a protocol error mentioning "input-1"
    And the dashboard shows a pending input "input-1" for role "coder" with prompt "Which branch should I use?" and freeform "false":
      | choice  |
      | main    |
      | develop |
    When the user responds to input "input-1" for role "coder" with answer "main" and freeform "false"
    Then the "coder" agent observes an input response for "input-1" with answer "main"
    And the dashboard receives a transcript update for role "coder" with source "user" and content "main"
    When the user responds to input "input-1" for role "coder" with answer "develop" and freeform "false"
    Then the user observes a protocol error mentioning "input-1"
    And the transcript for role "coder" does not contain "develop" within 2 seconds
    When the "coder" agent emits a system message "Deployment starting."
    Then the dashboard receives a transcript update for role "coder" with source "system" and content "Deployment starting."
    And every transcript update received for role "coder" reports a strictly increasing sequence and entry index
    When the user requests a fresh transcript synchronization for role "coder"
    Then the freshly synchronized transcript for role "coder" contains an entry with source "user" and content "main"

  Scenario: An accepted free-form input response is published once as a user transcript entry, ordered before later output, and survives synchronization
    When the "reviewer" agent requests input "input-2" with prompt "What should the commit message be?" and freeform "true":
      | choice |
    Then the dashboard shows a pending input "input-2" for role "reviewer" with prompt "What should the commit message be?" and freeform "true":
      | choice |
    When the user responds to input "input-2" for role "coder" with answer "wrong-role" and freeform "true"
    Then the user observes a protocol error mentioning "input-2"
    And the dashboard shows a pending input "input-2" for role "reviewer" with prompt "What should the commit message be?" and freeform "true":
      | choice |
    When the user responds to input "input-2" for role "reviewer" with answer "Fix the flaky retry test" and freeform "true"
    Then the "reviewer" agent observes an input response for "input-2" with answer "Fix the flaky retry test"
    And the dashboard receives a transcript update for role "reviewer" with source "user" and content "Fix the flaky retry test"
    When the user responds to input "input-2" for role "reviewer" with answer "Something else entirely" and freeform "true"
    Then the user observes a protocol error mentioning "input-2"
    And the transcript for role "reviewer" does not contain "Something else entirely" within 2 seconds
    When the "reviewer" agent emits a system message "Change committed."
    Then the dashboard receives a transcript update for role "reviewer" with source "system" and content "Change committed."
    And every transcript update received for role "reviewer" reports a strictly increasing sequence and entry index
    When the user requests a fresh transcript synchronization for role "reviewer"
    Then the freshly synchronized transcript for role "reviewer" contains an entry with source "user" and content "Fix the flaky retry test"

  Scenario: Identical request IDs remain independent across two roles
    When the "coder" agent requests permission "shared-request" with description "Run the deploy script?"
    And the "reviewer" agent requests permission "shared-request" with description "Publish the release notes?"
    Then the dashboard shows a pending permission "shared-request" for role "coder" with description "Run the deploy script?"
    And the dashboard shows a pending permission "shared-request" for role "reviewer" with description "Publish the release notes?"
    When the user responds to permission "shared-request" for role "coder" with approved "true"
    Then the "coder" agent observes a permission response for "shared-request" approved "true"
    And the dashboard shows a pending permission "shared-request" for role "reviewer" with description "Publish the release notes?"
    When the user responds to permission "shared-request" for role "reviewer" with approved "true"
    Then the "reviewer" agent observes a permission response for "shared-request" approved "true"
