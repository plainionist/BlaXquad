Feature: Headless UI selection
  squad-hq selects its native-window transport from an explicit "--ui" launch option, defaulting to Photino, and
  reports a clear diagnostic for every way the option can be misused.

  Scenario: A duplicate --ui option fails clearly
    When the executable launches with the ui option specified twice
    Then the launch fails with a UI diagnostic containing "may only be specified once"

  Scenario: A missing --ui value fails clearly
    When the executable launches with the ui option missing its value
    Then the launch fails with a UI diagnostic containing "requires a value"

  Scenario: An unknown --ui value fails clearly
    When the executable launches with an unknown ui value
    Then the launch fails with a UI diagnostic containing "must be 'photino' or 'stdio'"
