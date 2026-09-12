Feature: Delivering handoffs
  Headquarters persists each recipient's handoff before sending a wake-up.

  Background:
    Given `blaxquad/squad.json` configures:
      | role     |
      | coder    |
      | reviewer |
    When the operator launches Headquarters
    Then Headquarters starts an agent session for role "coder"
    And Headquarters starts an agent session for role "reviewer"

  Scenario: Deliver a handoff and notify its recipient
    Given "coder" prepares a note with priority "50" and message "Ready for review." to:
      | role     |
      | reviewer |
    When the "coder" role agent runs `squad handoff` from its worktree
    Then the sender handoff is archived as sent
    And "reviewer" has one new handoff
    And the new handoff for "reviewer" has recipient header "reviewer"
    And the "reviewer" agent observes the handoff wake-up message

  Scenario: Reject an invalid fan-out before delivering any copy
    When "coder" durably queues an invalid note to:
      | role     |
      | reviewer |
      | missing  |
    Then the sender handoff is archived as failed
    And "reviewer" has no new handoff
    And the "reviewer" agent has not observed the handoff wake-up message

  Scenario: Reject malformed handoff JSON before delivering any copy
    When "coder" durably queues a handoff with invalid content:
      """
      { "from": "coder",
      """
    Then the sender handoff is archived as failed
    And "reviewer" has no new handoff
    And the "reviewer" agent has not observed the handoff wake-up message

  Scenario: Reject a handoff whose kind and variant data disagree before delivering any copy
    When "coder" durably queues a handoff with invalid content:
      """
      {
        "id": "seed-mismatched-variant",
        "from": "coder",
        "to": ["reviewer"],
        "priority": 50,
        "kind": "note",
        "gitHandoff": { "task": "implement-search", "commit": "0123456789" },
        "createdAt": "2026-08-22T12:00:00Z"
      }
      """
    Then the sender handoff is archived as failed
    And "reviewer" has no new handoff
    And the "reviewer" agent has not observed the handoff wake-up message

  Scenario: Reject a handoff with a blank id before delivering any copy
    When "coder" durably queues a handoff with invalid content:
      """
      {
        "id": "",
        "from": "coder",
        "to": ["reviewer"],
        "priority": 50,
        "kind": "note",
        "note": { "message": "Ready for review." },
        "createdAt": "2026-08-22T12:00:00Z"
      }
      """
    Then the sender handoff is archived as failed
    And "reviewer" has no new handoff
    And the "reviewer" agent has not observed the handoff wake-up message

  Scenario: Reject a handoff with an out-of-range priority before delivering any copy
    When "coder" durably queues a handoff with invalid content:
      """
      {
        "id": "seed-out-of-range-priority",
        "from": "coder",
        "to": ["reviewer"],
        "priority": 100,
        "kind": "note",
        "note": { "message": "Ready for review." },
        "createdAt": "2026-08-22T12:00:00Z"
      }
      """
    Then the sender handoff is archived as failed
    And "reviewer" has no new handoff
    And the "reviewer" agent has not observed the handoff wake-up message

  Scenario: Reject a handoff with a noncanonical commit id before delivering any copy
    When "coder" durably queues a handoff with invalid content:
      """
      {
        "id": "seed-noncanonical-commit",
        "from": "coder",
        "to": ["reviewer"],
        "priority": 50,
        "kind": "git_handoff",
        "gitHandoff": { "task": "implement-search", "commit": "not-a-commit" },
        "createdAt": "2026-08-22T12:00:00Z"
      }
      """
    Then the sender handoff is archived as failed
    And "reviewer" has no new handoff
    And the "reviewer" agent has not observed the handoff wake-up message

  Scenario: Reject a handoff with a missing createdAt before delivering any copy
    When "coder" durably queues a handoff with invalid content:
      """
      {
        "id": "seed-missing-created-at",
        "from": "coder",
        "to": ["reviewer"],
        "priority": 50,
        "kind": "note",
        "note": { "message": "Ready for review." }
      }
      """
    Then the sender handoff is archived as failed
    And "reviewer" has no new handoff
    And the "reviewer" agent has not observed the handoff wake-up message

  Scenario: Reject a handoff with a malformed createdAt before delivering any copy
    When "coder" durably queues a handoff with invalid content:
      """
      {
        "id": "seed-malformed-created-at",
        "from": "coder",
        "to": ["reviewer"],
        "priority": 50,
        "kind": "note",
        "note": { "message": "Ready for review." },
        "createdAt": "not-a-timestamp"
      }
      """
    Then the sender handoff is archived as failed
    And "reviewer" has no new handoff
    And the "reviewer" agent has not observed the handoff wake-up message

  Scenario: Lifecycle timestamps survive delivery, claim, and completion
    Given "coder" prepares a note with priority "50" and message "Ready for review." to:
      | role     |
      | reviewer |
    When the "coder" role agent runs `squad handoff` from its worktree
    Then the sender handoff is archived as sent
    And "reviewer" has one new handoff
    And the new handoff for "reviewer" carries createdAt and enqueuedAt timestamps but no dequeuedAt or completedAt timestamp
    When the "reviewer" role agent claims the handoff via `squad ready-for-next`
    Then the in-process handoff for "reviewer" preserves its createdAt and enqueuedAt timestamps and now also carries a dequeuedAt timestamp
    When the "reviewer" role agent completes the handoff via `squad done-with-current`
    Then the completed handoff for "reviewer" preserves its createdAt, enqueuedAt, and dequeuedAt timestamps and now also carries a completedAt timestamp

  Scenario: Notification failure does not lose a delivered handoff
    Given the "reviewer" agent will reject its next harness send
    And "coder" prepares a note with priority "50" and message "Ready for review." to:
      | role     |
      | reviewer |
    When the "coder" role agent runs `squad handoff` from its worktree
    Then the sender handoff is archived as sent
    And "reviewer" has one new handoff
    And the "reviewer" agent's rejected harness send proves no wake-up was delivered
    And Headquarters remains available

  Scenario: A busy recipient's current prompt is not interrupted by a delivery wake-up
    Given "reviewer" is busy with a prompt
    And "coder" prepares a note with priority "50" and message "Ready for review." to:
      | role     |
      | reviewer |
    When the "coder" role agent runs `squad handoff` from its worktree
    Then the sender handoff is archived as sent
    And "reviewer" has one new handoff
    And the "reviewer" agent has not observed the handoff wake-up message
    When "reviewer" finishes its prompt
    Then the "reviewer" agent observes the handoff wake-up message
