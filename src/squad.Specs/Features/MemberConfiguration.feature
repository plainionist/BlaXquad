Feature: Configure and launch reusable roles with distinct members

  "blaxquad/squad.json" separates a reusable "role" (an immutable responsibility backed by a
  "blaxquad/roles/<role>.prompt" file) from a "member" (a uniquely named, independently launched participant that
  references exactly one role). Two members may deliberately reference the same role - each still gets its own
  worktree, provider session, and startup state, and both receive the instruction to read the same role prompt.
  Headquarters accepts only schema version 2 and rejects an invalid document - a duplicate member name, a member
  referencing an undeclared role, a role whose prompt file is missing, or a legacy version-1 document - with a
  specific diagnostic before any member session starts.

  Scenario: Two members sharing one role start independently and both read the same role prompt
    Given a backend scenario configured with role "coder" shared by members "coder-a,coder-b"
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for member "coder-a" across the control pipe
    And the backend scenario observes a session started for member "coder-b" across the control pipe
    And the "coder-a" agent observes a harness message containing "blaxquad/roles/coder.prompt"
    And the "coder-b" agent observes a harness message containing "blaxquad/roles/coder.prompt"
    And the backend scenario observes members "coder-a,coder-b" have distinct sessions
    And the backend scenario observes members "coder-a,coder-b" have distinct worktrees

  Scenario: A duplicate member name is rejected before any member session starts
    Given a backend scenario configured with roles "coder" and the raw configuration:
      """
      {
        "schemaVersion": 2,
        "leader": "coder",
        "roles": ["coder"],
        "members": [
          { "name": "coder", "role": "coder", "worktree": "master", "agent": {} },
          { "name": "coder", "role": "coder", "worktree": "master", "agent": {} }
        ]
      }
      """
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario attempts to start squad-hq with the fake provider fixture
    And the backend scenario waits for its rejected startup process to exit
    Then the backend scenario observes its rejected startup exited with a non-zero code
    And the backend scenario observes its rejected startup's standard error containing "Duplicate member 'coder'"
    And the backend scenario observes its rejected startup's standard error does not contain "Unhandled exception"
    And the backend scenario observes no session was ever started for member "coder"

  Scenario: A member referencing an undeclared role is rejected before any member session starts
    Given a backend scenario configured with roles "coder" and the raw configuration:
      """
      {
        "schemaVersion": 2,
        "leader": "coder",
        "roles": ["coder"],
        "members": [
          { "name": "coder", "role": "coder", "worktree": "master", "agent": {} },
          { "name": "reviewer", "role": "reviewer", "worktree": "master", "agent": {} }
        ]
      }
      """
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario attempts to start squad-hq with the fake provider fixture
    And the backend scenario waits for its rejected startup process to exit
    Then the backend scenario observes its rejected startup exited with a non-zero code
    And the backend scenario observes its rejected startup's standard error containing "references unknown role 'reviewer'"
    And the backend scenario observes its rejected startup's standard error does not contain "Unhandled exception"
    And the backend scenario observes no session was ever started for member "coder"
    And the backend scenario observes no session was ever started for member "reviewer"

  Scenario: A member referencing a role whose prompt file is missing is rejected before any member session starts
    Given a backend scenario configured with the raw configuration:
      """
      {
        "schemaVersion": 2,
        "leader": "coder",
        "roles": ["coder"],
        "members": [
          { "name": "coder", "role": "coder", "worktree": "master", "agent": {} }
        ]
      }
      """
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario attempts to start squad-hq with the fake provider fixture
    And the backend scenario waits for its rejected startup process to exit
    Then the backend scenario observes its rejected startup exited with a non-zero code
    And the backend scenario observes its rejected startup's standard error containing "Missing role prompt"
    And the backend scenario observes its rejected startup's standard error does not contain "Unhandled exception"
    And the backend scenario observes no session was ever started for member "coder"

  Scenario: A legacy version-1 configuration is rejected with an explicit identity-preserving migration diagnostic
    Given a backend scenario configured with the raw configuration:
      """
      {
        "leader": "coder",
        "roles": [
          { "name": "coder", "worktree": "master", "agent": {} }
        ]
      }
      """
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario attempts to start squad-hq with the fake provider fixture
    And the backend scenario waits for its rejected startup process to exit
    Then the backend scenario observes its rejected startup exited with a non-zero code
    And the backend scenario observes its rejected startup's standard error containing "must declare"
    And the backend scenario observes its rejected startup's standard error containing "schemaVersion"
    And the backend scenario observes its rejected startup's standard error containing "Migrate a version-1 configuration by keeping each old entry's"
    And the backend scenario observes its rejected startup's standard error does not contain "Unhandled exception"
    And the backend scenario observes no session was ever started for member "coder"
