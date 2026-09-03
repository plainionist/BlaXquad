Feature: Squad execution context
  Helper commands derive role identity from their current Git worktree.

  Scenario: Distinct role worktrees resolve their roles without a role environment variable
    Given a Git project with context roles "architect,implementer"
    When the "architect" worktree queries its role context without a legacy role environment variable
    Then the context role is "architect"
    When the "implementer" worktree queries its role context without a legacy role environment variable
    Then the context role is "implementer"

  Scenario: JSON context identifies the project, role worktree, and shared source
    Given a Git project with context roles "architect,implementer"
    When the "architect" worktree queries JSON context with a shared source path
    Then the JSON context identifies the "architect" role and its worktree