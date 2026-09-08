Feature: Backend-spec workspace and tool support
  Test-owned workspace support creates a uniquely rooted, configured Git project, locates role
  worktrees, and runs the exact published, provider-free squad-hq used by backend specifications
  - never a tool resolved from PATH, a checkout build, or an ambient headquarters process.

  Scenario: A role worktree runs the exact backend-spec squad-hq publication
    Given a configured project with role "architect"
    When the "architect" role worktree requests shutdown from the backend-spec squad-hq publication
    Then the backend-spec command succeeds

  Scenario: A failing backend-spec command reports full diagnostics
    Given a configured project with role "architect"
    When the "architect" role worktree runs an unknown backend-spec squad-hq command
    Then the backend-spec command fails
    And the failed command reports its executable, arguments, and working directory
