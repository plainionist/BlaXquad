Feature: Squad execution context

  Helper commands derive role identity from their current Git worktree.

  Scenario: Distinct role worktrees resolve their roles without a role environment variable

    Given `blaxquad/squad.json` configures:
      | role        |
      | architect   |
      | implementer |

    When the "architect" role agent runs `squad context` from its worktree without a legacy role environment variable

    Then the context role is "architect"

    When the "implementer" role agent runs `squad context` from its worktree without a legacy role environment variable

    Then the context role is "implementer"

  Scenario: JSON context identifies the project, role worktree, and shared source

    Given `blaxquad/squad.json` configures:
      | role        |
      | architect   |
      | implementer |

    When the "architect" role agent runs `squad context --json` from its worktree with a shared source path

    Then the JSON context identifies the "architect" role and its worktree