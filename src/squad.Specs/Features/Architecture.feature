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

  Scenario: Contract assemblies enforce the split boundaries
    Then squad.Abstractions is no longer part of the solution
    And the application core depends on the agent provider and UI abstractions but not on hosting or presentation adapters
    And the copilot sdk adapter depends only on the agent provider abstraction
    And the photino adapter depends on the UI and hosting abstractions but not directly on the agent provider or copilot sdk adapter
    And the agent provider and hosting abstractions do not depend on presentation or provider adapters

  Scenario: Handoff delivery is isolated in its own assembly
    Then the handoff delivery assembly depends only on agent configuration and handoff contracts
    And the application core no longer depends on agent configuration or handoff contracts

  Scenario: Transcript state is isolated in its own assembly
    Then the transcript assembly depends only on UI abstractions
    And the application core depends on the transcript assembly

  Scenario: Installers declare supported package targets
    Then the installers declare every supported runtime identifier
