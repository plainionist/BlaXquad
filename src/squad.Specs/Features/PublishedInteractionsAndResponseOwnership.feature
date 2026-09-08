Feature: Published interactions and response ownership

  Permission, input, and elicitation requests are published through the real "state.snapshot" UI protocol message
  with every supported field, a response sent through "permission.respond", "input.respond", or
  "elicitation.respond" reaches only the addressed role's fake session, wrong-role, duplicate, and late responses
  produce the documented "protocol.error", identical request IDs pending for two roles remain independent, and a
  role's pending interaction is cancelled by that role's abort, its session failing, or a host-control shutdown -
  observed only through the real UI protocol and the fake-agent control pipe, never through SquadViewModel, its
  pending-interaction collections, or "Recording*" objects.

  Background:
    Given a backend scenario configured with roles "coder,reviewer"
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    And the backend scenario observes a session started for role "reviewer" across the control pipe

  Scenario: Published UI state contains every supported field for permission, input, and elicitation requests
    When the "coder" agent requests permission "permission-1" with description "Run the deploy script?"
    Then the backend scenario observes a pending permission "permission-1" for role "coder" with description "Run the deploy script?"
    When the "coder" agent requests input "input-1" with prompt "Which branch should I use?" and choices "main,develop" and freeform "false"
    Then the backend scenario observes a pending input "input-1" for role "coder" with prompt "Which branch should I use?" and choices "main,develop" and freeform "false"
    When the "coder" agent requests elicitation "elicitation-1" with prompt "Confirm the deployment?" and mode "confirm"
    Then the backend scenario observes a pending elicitation "elicitation-1" for role "coder" with prompt "Confirm the deployment?" and mode "confirm"
    When the "coder" agent requests URL elicitation "elicitation-2" with prompt "Complete sign-in" and url "https://example.test/authorize"
    Then the backend scenario observes a pending elicitation "elicitation-2" for role "coder" with prompt "Complete sign-in" and mode "url" and url "https://example.test/authorize"

  Scenario: A successful response reaches only the owning role's fake session
    When the "coder" agent requests permission "permission-1" with description "Run the deploy script?"
    Then the backend scenario observes a pending permission "permission-1" for role "coder" with description "Run the deploy script?"
    When the backend scenario responds to permission "permission-1" for role "coder" with approved "true"
    Then the "coder" agent observes a permission response for "permission-1" approved "true"
    And the "reviewer" agent has not observed a permission response
    When the "reviewer" agent requests input "input-1" with prompt "Which branch should I use?"
    Then the backend scenario observes a pending input "input-1" for role "reviewer" with prompt "Which branch should I use?" and choices "" and freeform "true"
    When the backend scenario responds to input "input-1" for role "reviewer" with answer "main"
    Then the "reviewer" agent observes an input response for "input-1" with answer "main"
    And the "coder" agent has not observed an input response
    When the "coder" agent requests elicitation "elicitation-1" with prompt "Confirm the deployment?" and mode "confirm"
    Then the backend scenario observes a pending elicitation "elicitation-1" for role "coder" with prompt "Confirm the deployment?" and mode "confirm"
    When the backend scenario responds to elicitation "elicitation-1" for role "coder" with action "accept" and form value "yes"
    Then the "coder" agent observes an elicitation response for "elicitation-1" with action "accept" and form value "yes"
    And the "reviewer" agent has not observed an elicitation response

  Scenario: Wrong-role, duplicate, and late responses are rejected without disturbing the owner's pending request
    When the "coder" agent requests permission "permission-1" with description "Run the deploy script?"
    Then the backend scenario observes a pending permission "permission-1" for role "coder" with description "Run the deploy script?"
    When the backend scenario responds to permission "permission-1" for role "reviewer" with approved "true"
    Then the backend scenario observes a protocol error mentioning "permission-1"
    And the backend scenario observes a pending permission "permission-1" for role "coder" with description "Run the deploy script?"
    When the backend scenario responds to permission "permission-1" for role "coder" with approved "true"
    Then the "coder" agent observes a permission response for "permission-1" approved "true"
    When the backend scenario responds to permission "permission-1" for role "coder" with approved "true"
    Then the backend scenario observes a protocol error mentioning "permission-1"

  Scenario: Identical request IDs remain independent across two roles
    When the "coder" agent requests permission "shared-request" with description "Run the deploy script?"
    And the "reviewer" agent requests permission "shared-request" with description "Publish the release notes?"
    Then the backend scenario observes a pending permission "shared-request" for role "coder" with description "Run the deploy script?"
    And the backend scenario observes a pending permission "shared-request" for role "reviewer" with description "Publish the release notes?"
    When the backend scenario responds to permission "shared-request" for role "coder" with approved "true"
    Then the "coder" agent observes a permission response for "shared-request" approved "true"
    And the backend scenario observes a pending permission "shared-request" for role "reviewer" with description "Publish the release notes?"
    When the backend scenario responds to permission "shared-request" for role "reviewer" with approved "true"
    Then the "reviewer" agent observes a permission response for "shared-request" approved "true"

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
