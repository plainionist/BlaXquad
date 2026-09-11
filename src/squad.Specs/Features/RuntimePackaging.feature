Feature: Headquarters runtime packaging
  squad-hq ships one squad.Runtime assembly that owns Headquarters lifecycle and process-control
  behavior; the retired squad.Host.Runtime and squad.Host.Control assemblies must never reappear
  in a clean publish.

  Scenario: The published squad-hq output contains one Headquarters runtime assembly
    Then the published squad-hq output contains the squad.Runtime assembly
    And the published squad-hq output contains no squad.Host.Runtime or squad.Host.Control assembly
