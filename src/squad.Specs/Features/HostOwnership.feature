Feature: Squad host ownership
  A project has one authoritative host owner.

  Scenario: A host writes ownership metadata
    Given the project host lease is acquired
    Then host metadata exists
    And host metadata names the project root

  Scenario: A duplicate host is rejected while the lease is held
    Given the project host lease is acquired
    When a second project host lease is acquired
    Then the second host acquisition fails
    And host metadata still exists

  Scenario: A duplicate executable launch fails clearly
    Given the project host lease is acquired
    When the executable attempts a duplicate launch
    Then the duplicate launch fails without an exception trace

  Scenario: The executable shuts down an owned host
    Given the project host lease is acquired
    When the executable requests squad shutdown
    Then the executable shutdown succeeds
    And host metadata is absent
    And the host lock can be reacquired

  Scenario: Shutdown removes stale metadata and remains idempotent
    Given stale host metadata exists
    When the executable requests squad shutdown
    Then the executable shutdown succeeds
    And host metadata is absent
    When the executable requests squad shutdown again
    Then the executable shutdown succeeds

  Scenario: Invalid control requests do not fault the control server
    Given the project host lease is acquired
    When an invalid control request is sent
    Then the invalid control request is rejected
    When a malformed control request is sent
    Then the malformed control request is rejected
    When a ping control request is sent
    Then the control server remains available

  Scenario: Waiting for an agent blocks until the live host reports it ready
    Given the project host lease is acquired
    And the "architect" agent is not ready
    When the executable begins waiting for the "architect" agent
    Then the executable remains waiting for agent readiness
    When the "architect" agent becomes ready
    Then the agent readiness wait succeeds

  Scenario: Waiting for a busy agent times out clearly
    Given the project host lease is acquired
    And the "architect" agent is not ready
    When the executable waits 3.0 seconds for the "architect" agent
    Then the agent readiness wait times out

  Scenario: Waiting for an unknown agent fails clearly
    Given the project host lease is acquired
    And the "architect" agent is not ready
    When the executable waits 1 seconds for the "reviewer" agent
    Then the agent readiness wait reports an unknown role

  Scenario: Waiting from the main checkout discovers its host
    Given a Git project host with a ready "architect" agent
    When the executable waits for "architect" without an explicit project root
    Then the agent readiness command succeeds

  Scenario: Waiting from a linked worktree discovers the main host
    Given a Git project host with a ready "architect" agent
    And an "architect" linked worktree
    When the executable waits for "architect" from the linked worktree
    Then the agent readiness command succeeds

  Scenario: Waiting with an equivalent explicit project path reaches the host
    Given a Git project host with a ready "architect" agent
    When the executable waits for "architect" using an equivalent project path
    Then the agent readiness command succeeds

  Scenario: Waiting outside a squad project fails before polling
    Given an empty project
    When the executable waits for "architect" without an explicit project root
    Then project root discovery fails promptly

  Scenario: A zero readiness timeout is rejected before project discovery
    Given an empty project
    When the executable waits with a zero timeout for "architect"
    Then the zero readiness timeout is rejected

  Scenario: An unavailable host control endpoint respects the readiness deadline
    Given the project host lock is held without a control server
    When the host client waits 0.1 seconds for the "architect" agent
    Then the unavailable control wait respects the deadline

  Scenario: Shutdown accepts an equivalent project path
    Given the project host lease is acquired
    When the executable requests shutdown for an equivalent project path
    Then the executable shutdown succeeds
    And host metadata is absent

  Scenario: Shutdown is idempotent for an empty project
    Given an empty project
    When the executable requests shutdown for the empty project
    Then the executable shutdown succeeds
