Feature: Headquarters ownership

  A project has one authoritative Headquarters owner.

  Scenario: A duplicate launch fails clearly

    Given Headquarters is running with an auto-echoing "architect" agent

    When the operator attempts a duplicate launch

    Then the duplicate launch fails without an exception trace

    When the user sends "still there?" to role "architect"

    Then the transcript for role "architect" contains "echo: still there?"

    When the operator shuts down Headquarters

    Then Headquarters exits with code 0

  Scenario: Shutdown accepts an equivalent project path

    Given Headquarters is running

    When the operator requests shutdown for an equivalent project path
    And Headquarters' process exits on its own

    Then Headquarters exits with code 0

  Scenario: A replacement Headquarters instance recovers after an abrupt termination

    Given Headquarters is running

    When the Headquarters process is abruptly terminated
    And the operator launches a new Headquarters against the same project

    Then the new Headquarters process reports ready

  Scenario: Waiting for an agent blocks until the live Headquarters instance reports it ready

    Given Headquarters is running in a Git project with a busy "architect" agent

    When the operator begins waiting for role "architect" to become ready with `squad-hq wait-for-agent`

    Then role "architect"'s readiness wait remains pending

    When the "architect" agent emits idle

    Then role "architect"'s readiness wait succeeds

  Scenario: Waiting for a busy agent times out clearly

    Given Headquarters is running in a Git project with a busy "architect" agent

    When the operator waits 3.0 seconds for role "architect" to become ready with `squad-hq wait-for-agent`

    Then the agent readiness wait times out

  Scenario: Waiting for an unknown agent fails clearly

    Given Headquarters is running in a Git project with a ready "architect" agent

    When the operator waits 1 seconds for role "reviewer" to become ready with `squad-hq wait-for-agent`

    Then the agent readiness wait reports an unknown role

  Scenario: Waiting from the main checkout discovers Headquarters

    Given Headquarters is running in a Git project with a ready "architect" agent

    When the operator waits for role "architect" to become ready with `squad-hq wait-for-agent` without an explicit project root

    Then the agent readiness command succeeds

  Scenario: Waiting from a linked worktree discovers the main Headquarters instance

    Given Headquarters is running in a Git project with a ready "architect" agent
    And an "architect" linked worktree

    When the operator waits for role "architect" to become ready with `squad-hq wait-for-agent` from the linked worktree

    Then the agent readiness command succeeds

  Scenario: Waiting with an equivalent explicit project path reaches Headquarters

    Given Headquarters is running in a Git project with a ready "architect" agent

    When the operator waits for role "architect" to become ready with `squad-hq wait-for-agent` using an equivalent project path

    Then the agent readiness command succeeds

  Scenario: Waiting outside a squad project fails before polling

    Given an empty project

    When the operator waits for role "architect" to become ready with `squad-hq wait-for-agent` without an explicit project root

    Then project root discovery fails promptly

  Scenario: A zero readiness timeout is rejected before project discovery

    Given an empty project

    When the operator waits for role "architect" to become ready with `squad-hq wait-for-agent` with a zero timeout

    Then the zero readiness timeout is rejected

  Scenario: Waiting after Headquarters has terminated respects the readiness deadline

    Given Headquarters is running

    When the Headquarters process is abruptly terminated
    And the operator waits 1 seconds for role "architect" to become ready with `squad-hq wait-for-agent`

    Then the readiness wait reports Headquarters as unavailable

  Scenario: Shutdown is idempotent for an empty project

    Given an empty project

    When the operator requests shutdown for the empty project

    Then the operator's shutdown succeeds
