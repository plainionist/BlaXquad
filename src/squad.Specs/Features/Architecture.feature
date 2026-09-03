Feature: Runtime assembly boundaries
  Technology-specific implementations stay outside the shared core.

  Scenario: Shared runtime code remains technology neutral
    Then the shared core assembly has no technology package references
    And technology implementation files are outside the core project
    And headquarters owns runtime composition without a bootstrap project
    And production source does not use the legacy backend selector

  Scenario: Every agent executable dependency is agent-safe
    Then every reachable squad production assembly and type is agent-safe
    And no headquarters-only helper is reachable from squad
    And no host lifecycle, backend runtime, or UI contract is reachable from squad
    And handoff, context, ready-for-next, and done-with-current remain available
    And squad-hq retains launch, shutdown, and wait-for-agent behavior

  Scenario: Installers declare supported package targets
    Then the installers declare every supported runtime identifier
