Feature: Agent provider selection
  squad-hq loads the agent provider used for a session from an explicit "--provider" descriptor,
  falling back to the packaged default provider, without depending on the workspace to decide,
  and reports a clear, non-crashing diagnostic for every way selection can fail.

  Scenario: A missing provider assembly fails clearly
    When the operator launches Headquarters with a provider assembly that does not exist
    Then the launch fails with a provider diagnostic containing "not found"

  Scenario: An incompatible provider type fails clearly
    When the operator launches Headquarters with an incompatible provider type
    Then the launch fails with a provider diagnostic containing "does not publicly implement"

  Scenario: A duplicate --provider option fails clearly
    When the operator launches Headquarters with the provider option specified twice
    Then the launch fails with a provider diagnostic containing "may only be specified once"

  Scenario: A provider constructor failure is surfaced clearly
    When the operator launches Headquarters with a provider whose constructor throws
    Then the launch fails with a provider diagnostic containing "threw during construction"
