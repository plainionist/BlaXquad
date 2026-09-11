Feature: Workspace and configuration failures terminate at the command boundary

  A real squad-hq process launched against an invalid workspace or configuration - a missing or malformed project
  configuration file, a missing constitution prompt, a missing required helper script, or a shared worktree path
  that is not safe to replace - reports the specific operator-facing diagnostic on standard error exactly once
  (never labeled as a generic provider failure, never a raw ".NET Unhandled exception" dump), exits with a
  non-zero code, never reaches readiness, preserves durable workspace state it does not own, and permits a fresh,
  healthy launch once the underlying problem is fixed - proven only through the real process, never through
  WorkspacePreparer directly.

  Scenario: A missing project configuration file reports a clear diagnostic and permits a healthy relaunch once fixed
    Given `blaxquad/squad.json` configures:
      | role  |
      | coder |
    And role "coder" has a durable file "notes.md" containing "Keep this note."
    And the project configuration file is missing
    When the operator launches Headquarters without completing the ready handshake
    And Headquarters' process exits on its own
    Then Headquarters exits with a non-zero code
    And Headquarters' standard error contains "Config not found at"
    And Headquarters' standard error does not contain "Provider startup failed"
    And Headquarters' standard error does not contain "Unhandled exception"
    And role "coder"'s durable file "notes.md" still contains "Keep this note."
    When the project configuration file is restored
    And the operator launches a new Headquarters against the same project
    Then the new Headquarters process reports ready

  Scenario: A malformed project configuration file reports a clear diagnostic
    Given `blaxquad/squad.json` configures:
      | role  |
      | coder |
    And role "coder" has a durable file "notes.md" containing "Keep this note."
    And the project configuration file is malformed
    When the operator launches Headquarters without completing the ready handshake
    And Headquarters' process exits on its own
    Then Headquarters exits with a non-zero code
    And Headquarters' standard error contains "invalid JSON"
    And Headquarters' standard error does not contain "Provider startup failed"
    And Headquarters' standard error does not contain "Unhandled exception"
    And role "coder"'s durable file "notes.md" still contains "Keep this note."

  Scenario: A missing constitution prompt reports a clear diagnostic
    Given `blaxquad/squad.json` configures:
      | role  |
      | coder |
    And role "coder" has a durable file "notes.md" containing "Keep this note."
    And the constitution prompt file is missing
    When the operator launches Headquarters without completing the ready handshake
    And Headquarters' process exits on its own
    Then Headquarters exits with a non-zero code
    And Headquarters' standard error contains "Constitution prompt not found at"
    And Headquarters' standard error does not contain "Provider startup failed"
    And Headquarters' standard error does not contain "Unhandled exception"
    And role "coder"'s durable file "notes.md" still contains "Keep this note."

  Scenario: A non-empty shared worktree path reports a clear diagnostic instead of discarding its content
    Given `blaxquad/squad.json` configures:
      | role  |
      | coder |
    And role "coder" has a durable file "notes.md" containing "Keep this note."
    And role "coder"'s worktree already has non-empty directory "shared" configured as a shared worktree path
    When the operator launches Headquarters without completing the ready handshake
    And Headquarters' process exits on its own
    Then Headquarters exits with a non-zero code
    And Headquarters' standard error contains "Cannot replace non-empty shared worktree path"
    And Headquarters' standard error does not contain "Provider startup failed"
    And Headquarters' standard error does not contain "Unhandled exception"
    And role "coder"'s durable file "notes.md" still contains "Keep this note."

  Scenario: A continued launch rejects a legacy handoff queue instead of processing it
    Given `blaxquad/squad.json` configures:
      | role  |
      | coder |
    And role "coder" has a durable file ".blaxquad/handoffs/outbox/50_legacy_from_coder_to_reviewer.handoff" containing "legacy queue artifact"
    When the operator launches Headquarters, continuing from durable state, without completing the ready handshake
    And Headquarters' process exits on its own
    Then Headquarters exits with a non-zero code
    And Headquarters' standard error contains "LEGACY_HANDOFF_QUEUE"
    And Headquarters' standard error does not contain "Provider startup failed"
    And Headquarters' standard error does not contain "Unhandled exception"
    And role "coder"'s durable file ".blaxquad/handoffs/outbox/50_legacy_from_coder_to_reviewer.handoff" still contains "legacy queue artifact"

  Scenario: A continued launch rejects a mixed legacy and JSON handoff queue without delivering the JSON sibling
    Given `blaxquad/squad.json` configures:
      | role     |
      | coder    |
      | reviewer |
    And role "coder" has a durable file ".blaxquad/handoffs/outbox/50_legacy_from_coder_to_reviewer.handoff" containing "legacy queue artifact"
    And role "coder" has a durable file ".blaxquad/handoffs/outbox/60_valid_from_coder_to_reviewer.handoff.json" containing:
      """
      {
        "schemaVersion": 1,
        "id": "mixed-queue-sibling",
        "from": "coder",
        "to": ["reviewer"],
        "priority": 60,
        "kind": "note",
        "note": { "message": "Sibling JSON handoff." },
        "createdAt": "2026-08-22T12:00:00Z"
      }
      """
    When the operator launches Headquarters, continuing from durable state, without completing the ready handshake
    And Headquarters' process exits on its own
    Then Headquarters exits with a non-zero code
    And Headquarters' standard error contains "LEGACY_HANDOFF_QUEUE"
    And role "coder"'s durable file ".blaxquad/handoffs/outbox/50_legacy_from_coder_to_reviewer.handoff" still contains "legacy queue artifact"
    And role "coder"'s durable file ".blaxquad/handoffs/outbox/60_valid_from_coder_to_reviewer.handoff.json" still contains:
      """
      {
        "schemaVersion": 1,
        "id": "mixed-queue-sibling",
        "from": "coder",
        "to": ["reviewer"],
        "priority": 60,
        "kind": "note",
        "note": { "message": "Sibling JSON handoff." },
        "createdAt": "2026-08-22T12:00:00Z"
      }
      """
    And "reviewer" has no new handoff

  Scenario: A missing required helper script reports a clear diagnostic
    Given `blaxquad/squad.json` configures:
      | role  |
      | coder |
    And role "coder" has a durable file "notes.md" containing "Keep this note."
    When the operator launches Headquarters from a deployment missing its required helper script
    And Headquarters' process exits on its own
    Then Headquarters exits with a non-zero code
    And Headquarters' standard error contains "Required helper script not found or not executable"
    And Headquarters' standard error does not contain "Provider startup failed"
    And Headquarters' standard error does not contain "Unhandled exception"
    And role "coder"'s durable file "notes.md" still contains "Keep this note."
