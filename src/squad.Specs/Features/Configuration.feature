Feature: Squad configuration
  A project defines its roles and receive modes in one readable configuration.

  Background:
    Given a constitution prompt exists

  Scenario: A valid topology becomes runtime role state
    Given this squad configuration:
      """
      {
        "roles": [
          { "name": "coder", "worktree": "master", "agent": {} },
          { "name": "reviewer", "worktree": "review", "receiveMode": "batch", "agent": { "permissions": "approveAll" } }
        ]
      }
      """
    And role prompts exist for "coder,reviewer"
    When the squad configuration is parsed
    Then the command succeeds
    And runtime role "coder" uses worktree "master" and receive mode "task"
    And runtime role "reviewer" uses worktree "review" and receive mode "batch"
    And standard output contains "reviewer Reviewer"
    And standard output contains "permissions=approveAll"

  Scenario: Role names containing underscores are rejected
    Given this squad configuration:
      """
      { "roles": [ { "name": "code_reviewer", "worktree": "master", "agent": {} } ] }
      """
    And role prompts exist for "code_reviewer"
    When the squad configuration is parsed
    Then the command fails
    And standard error contains "role names may not contain underscores"

  Scenario: Every configured role requires a prompt
    Given this squad configuration:
      """
      { "roles": [ { "name": "coder", "worktree": "master", "agent": {} } ] }
      """
    When the squad configuration is parsed
    Then the command fails
    And standard error contains "Missing role prompt"

  Scenario: Explicit agent settings map to SDK session settings
    Given this squad configuration:
      """
      { "roles": [ { "name": "coder", "worktree": "master", "agent": { "permissions": "approveAll", "model": "gpt-5", "effort": "high" } } ] }
      """
    And role prompts exist for "coder"
    When the squad configuration is parsed
    Then the command succeeds
    And standard output contains "permissions=approveAll"
    And standard output contains "model=gpt-5"
    And standard output contains "effort=high"

  Scenario: Unknown JSON properties are rejected
    Given this squad configuration:
      """
      { "roles": [ { "name": "coder", "worktree": "master", "agent": {}, "recieveMode": "batch" } ] }
      """
    And role prompts exist for "coder"
    When the squad configuration is parsed
    Then the command fails
    And standard error contains "could not be mapped"

  Scenario: Invalid agent permissions are rejected
    Given this squad configuration:
      """
      { "roles": [ { "name": "coder", "worktree": "master", "agent": { "permissions": "always" } } ] }
      """
    And role prompts exist for "coder"
    When the squad configuration is parsed
    Then the command fails
    And standard error contains "expected prompt or approveAll"

  Scenario: Duplicate worktrees are rejected
    Given this squad configuration:
      """
      { "roles": [ { "name": "coder", "worktree": "shared", "agent": {} }, { "name": "reviewer", "worktree": "shared", "agent": {} } ] }
      """
    And role prompts exist for "coder,reviewer"
    When the squad configuration is parsed
    Then the command fails
    And standard error contains "Duplicate worktree"

  Scenario: Overlapping shared worktree paths are rejected
    Given this squad configuration:
      """
      {
        "sharedWorktreePaths": ["shared", "shared/child"],
        "roles": [ { "name": "coder", "worktree": "master", "agent": {} } ]
      }
      """
    And role prompts exist for "coder"
    When the squad configuration is parsed
    Then the command fails
    And standard error contains "Overlapping shared worktree path"

  Scenario: Empty role arrays are rejected
    Given this squad configuration:
      """
      { "roles": [] }
      """
    When the squad configuration is parsed
    Then the command fails
    And standard error contains "non-empty roles array"
