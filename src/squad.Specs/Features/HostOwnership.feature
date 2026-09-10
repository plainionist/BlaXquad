Feature: Squad host ownership
  A project has one authoritative host owner.

  Scenario: A duplicate launch fails clearly
    Given a squad host is running
    When the operator attempts a duplicate launch
    Then the duplicate launch fails without an exception trace
    And the original host still answers a public command
    When the operator requests squad shutdown
    Then the operator's shutdown succeeds
    And the host process exits

  Scenario: Shutdown accepts an equivalent project path
    Given a squad host is running
    When the operator requests shutdown for an equivalent project path
    Then the operator's shutdown succeeds
    And the host process exits

  Scenario: A replacement host recovers after an abrupt termination
    Given a squad host is running
    When the host process is abruptly terminated
    Then a new host can be started for the same project

  Scenario: Waiting for an agent blocks until the live host reports it ready
    Given a Git project host with a busy "architect" agent
    When the operator begins waiting for the "architect" agent
    Then the operator remains waiting for agent readiness
    When the "architect" agent becomes ready
    Then the agent readiness wait succeeds

  Scenario: Waiting for a busy agent times out clearly
    Given a Git project host with a busy "architect" agent
    When the operator waits 3.0 seconds for the "architect" agent
    Then the agent readiness wait times out

  Scenario: Waiting for an unknown agent fails clearly
    Given a Git project host with a ready "architect" agent
    When the operator waits 1 seconds for the "reviewer" agent
    Then the agent readiness wait reports an unknown role

  Scenario: Waiting from the main checkout discovers its host
    Given a Git project host with a ready "architect" agent
    When the operator waits for "architect" without an explicit project root
    Then the agent readiness command succeeds

  Scenario: Waiting from a linked worktree discovers the main host
    Given a Git project host with a ready "architect" agent
    And an "architect" linked worktree
    When the operator waits for "architect" from the linked worktree
    Then the agent readiness command succeeds

  Scenario: Waiting with an equivalent explicit project path reaches the host
    Given a Git project host with a ready "architect" agent
    When the operator waits for "architect" using an equivalent project path
    Then the agent readiness command succeeds

  Scenario: Waiting outside a squad project fails before polling
    Given an empty project
    When the operator waits for "architect" without an explicit project root
    Then project root discovery fails promptly

  Scenario: A zero readiness timeout is rejected before project discovery
    Given an empty project
    When the operator waits with a zero timeout for "architect"
    Then the zero readiness timeout is rejected

  Scenario: Waiting after the host has terminated respects the readiness deadline
    Given a squad host is running
    When the host process is abruptly terminated
    And the operator waits 1 seconds for the "architect" agent
    Then the readiness wait reports the host as unavailable

  Scenario: Shutdown is idempotent for an empty project
    Given an empty project
    When the operator requests shutdown for the empty project
    Then the operator's shutdown succeeds
