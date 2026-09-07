Feature: Agent provider packaging
  squad-hq takes on the default Copilot provider, its managed dependencies, and its native
  runtime assets only as publish-time packaging inputs - never as a compile-time dependency -
  and an opt-out publish can omit them entirely while still producing a working package.

  Scenario: squad-hq has no compile-time dependency on the Copilot provider
    Then the published squad-hq assembly has no compile-time dependency on squad.CopilotSdk

  Scenario: The default publish packages the Copilot provider and its runtime assets
    Then the published squad-hq output contains the Copilot provider assembly
    And the published squad-hq output contains the Copilot native runtime assets

  Scenario: An opt-out publish omits the Copilot provider entirely
    When squad-hq is published with the Copilot provider opted out
    Then the opt-out publish output contains no Copilot provider assembly or runtime assets
