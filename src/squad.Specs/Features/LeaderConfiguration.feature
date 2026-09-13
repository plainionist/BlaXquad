Feature: Configured squad leader

  "blaxquad/squad.json" always has an authoritative leader: an explicit top-level "leader" whose value must exactly
  match one configured member's name, or - when "leader" is omitted or blank - the first configured member.
  Because a role may be shared by more than one member, an explicit leader naming a declared role that is not
  itself a member name is rejected, even when that role happens to have only one member. Headquarters rejects
  startup with a clear diagnostic before creating any member session only when an explicitly configured leader does
  not match any configured member, and otherwise publishes the leader as authoritative "state.snapshot" metadata
  independent of configured member order.

  Scenario: An omitted leader defaults to the first configured member

    Given a backend scenario configured with roles "coder" and the raw configuration:
      """
      {
        "schemaVersion": 2,
        "roles": ["coder"],
        "members": [
          { "name": "coder-b", "role": "coder", "worktree": "coder-b", "agent": {} },
          { "name": "coder-a", "role": "coder", "worktree": "coder-a", "agent": {} }
        ]
      }
      """

    When the backend scenario starts squad-hq with the fake provider fixture

    Then the backend scenario observes state.snapshot leader as "coder-b"
    And the backend scenario observes state.snapshot roles reported in order "coder-b,coder-a"

  Scenario: A blank leader defaults to the first configured member

    Given a backend scenario configured with roles "coder" and the raw configuration:
      """
      {
        "schemaVersion": 2,
        "leader": "",
        "roles": ["coder"],
        "members": [
          { "name": "coder-b", "role": "coder", "worktree": "coder-b", "agent": {} },
          { "name": "coder-a", "role": "coder", "worktree": "coder-a", "agent": {} }
        ]
      }
      """

    When the backend scenario starts squad-hq with the fake provider fixture

    Then the backend scenario observes state.snapshot leader as "coder-b"

  Scenario: An explicit leader naming a declared role instead of a member is rejected before any member session starts

    Given a backend scenario configured with roles "coder" and the raw configuration:
      """
      {
        "schemaVersion": 2,
        "leader": "coder",
        "roles": ["coder"],
        "members": [
          { "name": "coder-a", "role": "coder", "worktree": "coder-a", "agent": {} },
          { "name": "coder-b", "role": "coder", "worktree": "coder-b", "agent": {} }
        ]
      }
      """
    And the backend scenario has enabled the fake-provider control transport

    When the backend scenario attempts to start squad-hq with the fake provider fixture
    And the backend scenario waits for its rejected startup process to exit

    Then the backend scenario observes its rejected startup exited with a non-zero code
    And the backend scenario observes its rejected startup's standard error containing "leader 'coder'"
    And the backend scenario observes its rejected startup's standard error does not contain "Unhandled exception"
    And the backend scenario observes no session was ever started for member "coder-a"
    And the backend scenario observes no session was ever started for member "coder-b"

  Scenario: A valid leader naming a member other than the first is published in state.snapshot independent of member order

    Given a backend scenario configured with roles "coder" and the raw configuration:
      """
      {
        "schemaVersion": 2,
        "leader": "coder-b",
        "roles": ["coder"],
        "members": [
          { "name": "coder-a", "role": "coder", "worktree": "coder-a", "agent": {} },
          { "name": "coder-b", "role": "coder", "worktree": "coder-b", "agent": {} }
        ]
      }
      """

    When the backend scenario starts squad-hq with the fake provider fixture

    Then the backend scenario observes state.snapshot leader as "coder-b"
    And the backend scenario observes state.snapshot roles reported in order "coder-a,coder-b"
