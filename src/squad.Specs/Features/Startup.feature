Feature: Preparing squad startup
  Startup validates the topology and writes the durable state needed by tools.

  Scenario: Prepare role state for a valid squad
    Given a constitution prompt exists
    And this squad configuration:
      """
      {
        "roles": [
          { "name": "coordinator", "worktree": "master", "agent": {} },
          { "name": "coder", "worktree": "coder", "receiveMode": "task", "agent": {} }
        ]
      }
      """
    And role prompts exist for "coordinator,coder"
    When startup state is prepared
    Then the command succeeds
    And runtime role "coder" uses worktree "coder" and receive mode "task"
    And runtime role "coordinator" uses worktree "master" and receive mode "task"

  Scenario: Launch resets configured worktrees and clears their handoff queues
    Given a constitution prompt exists
    And this squad configuration:
      """
      {
        "roles": [
          { "name": "coordinator", "worktree": "master", "agent": {} },
          { "name": "coder", "worktree": "coder", "agent": {} }
        ]
      }
      """
    And role prompts exist for "coordinator,coder"
    And the configured "coder" worktree has local changes and queued handoffs
    When launch preparation runs
    Then the configured "coder" worktree has no local changes
    And the configured "coder" worktree has no queued handoffs

  Scenario: Continuing a launch preserves configured worktree state
    Given a constitution prompt exists
    And this squad configuration:
      """
      {
        "roles": [
          { "name": "coordinator", "worktree": "master", "agent": {} },
          { "name": "coder", "worktree": "coder", "agent": {} }
        ]
      }
      """
    And role prompts exist for "coordinator,coder"
    And the configured "coder" worktree has local changes and queued handoffs
    When launch preparation continues
    Then the configured "coder" worktree retains local changes
    And the configured "coder" worktree retains queued handoffs

  Scenario: Launch shares configured directories with dedicated worktrees
    Given a constitution prompt exists
    And this squad configuration:
      """
      {
        "sharedWorktreePaths": ["agent_context"],
        "roles": [
          { "name": "coordinator", "worktree": "master", "agent": {} },
          { "name": "coder", "worktree": "coder", "agent": {} }
        ]
      }
      """
    And role prompts exist for "coordinator,coder"
    And the configured "coder" worktree has an empty replacement for shared path "agent_context"
    When launch preparation runs
    Then the configured "coder" worktree shares path "agent_context" with the root repository
    When launch preparation continues
    Then the configured "coder" worktree shares path "agent_context" with the root repository
