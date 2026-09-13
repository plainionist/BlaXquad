Feature: Hosting adapter packaging

  squad-hq takes on the default Photino hosting plug-in, its managed dependencies, native runtime assets, Vue
  distribution, and window assets only as build- and publish-time packaging inputs - never as a compile-time
  dependency - and an opt-out publish can omit it entirely while still producing a working package that never
  carries the test-only stdio hosting plug-in either.

  Scenario: squad-hq has no compile-time dependency on either concrete hosting assembly

    Then the published squad-hq assembly has no compile-time dependency on a concrete hosting assembly

  Scenario: The default publish packages the Photino hosting plug-in, its runtime assets, and its UI

    Then the published squad-hq output contains the Photino hosting assembly and its dependency manifest
    And the published squad-hq output contains the Photino native runtime assets
    And the published squad-hq output contains the Vue dashboard and window icon
    And the published squad-hq output contains no stdio hosting plug-in

  Scenario: An opt-out publish omits the Photino hosting plug-in entirely

    When squad-hq is published with the Photino hosting plug-in opted out

    Then the opt-out publish output contains no Photino hosting assembly, runtime assets, or UI

  Scenario: The backend-spec publication is a hosting-neutral squad-hq

    Then the published backend-spec output contains the squad-hq executable
    And the published backend-spec output contains no Photino hosting assembly, dependency, runtime asset, Vue distribution, or window asset
    And the published backend-spec output contains no stdio hosting plug-in
