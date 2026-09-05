Feature: Runtime assembly boundaries
  Technology-specific implementations stay outside the application model.

  Scenario: Application runtime code remains technology neutral
    Then the application assembly has no technology package references
    And technology implementation files are outside the application project
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
    And the application assembly depends on the agent provider and UI abstractions but not on hosting or presentation adapters
    And the copilot sdk adapter depends only on the agent provider abstraction
    And the photino adapter depends on the UI and hosting abstractions but not directly on the agent provider or copilot sdk adapter
    And the agent provider and hosting abstractions do not depend on presentation or provider adapters

  Scenario: Handoff delivery is isolated in its own assembly
    Then the handoff delivery assembly depends only on agent configuration and handoff contracts
    And the application assembly no longer depends on agent configuration or handoff contracts

  Scenario: Transcript state is isolated in its own assembly
    Then the transcript assembly depends only on UI abstractions
    And the application assembly depends on the transcript assembly

  Scenario: The application assembly boundary is enforced
    Then the application assembly depends on exactly the agent provider abstraction, the UI abstraction, and the transcript assembly
    And the transcript and handoff assemblies do not depend on the application assembly
    And headquarters composes the application assembly, transcript assembly, and handoff assembly without a reverse dependency
    And role-operation, interaction, and event-projection coordinators are internal modules owned by the application assembly
    And transcript and handoff implementation types are owned only by their extracted assemblies
    And no squad.Core project identity remains in the supported solution and project graph

  Scenario: SquadApplication owns only the outer run loop and process-wide resources
    Then SquadApplication has no generation session, observer, backend runtime, or handoff production lifecycle fields

  Scenario: Installers declare supported package targets
    Then the installers declare every supported runtime identifier
