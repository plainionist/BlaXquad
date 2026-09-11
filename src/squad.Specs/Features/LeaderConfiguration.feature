Feature: Configured squad leader

  "blaxquad/squad.json" always has an authoritative leader: an explicit top-level "leader" whose value must exactly
  match one configured role name, or - when "leader" is omitted or blank - the first role in the configured "roles"
  array. Headquarters rejects startup with a clear diagnostic before creating any role session only when an
  explicitly configured leader does not match any configured role, and otherwise publishes the leader as
  authoritative "state.snapshot" metadata independent of configured role order.

  Scenario: An omitted leader defaults to the first configured role
    Given a backend scenario configured with roles "coder,architect" and no leader
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes state.snapshot leader as "coder"
    And the backend scenario observes state.snapshot roles reported in order "coder,architect"

  Scenario: A blank leader defaults to the first configured role
    Given a backend scenario configured with roles "coder,architect" and leader ""
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes state.snapshot leader as "coder"

  Scenario: A leader that does not match any configured role is rejected before any role session starts
    Given a backend scenario configured with roles "coder,architect" and leader "reviewer"
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario launches squad-hq with the fake provider fixture without completing the ready handshake
    And the backend scenario waits for the process to exit on its own
    Then the backend scenario observes a non-zero exit code
    And the backend scenario observes standard error containing "leader"
    And the backend scenario observes standard error does not contain "Unhandled exception"
    And the backend scenario observes no session was ever started for role "coder"
    And the backend scenario observes no session was ever started for role "architect"

  Scenario: A valid leader is published in state.snapshot independent of configured role order
    Given a backend scenario configured with roles "coder,architect" and leader "architect"
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes state.snapshot leader as "architect"
    And the backend scenario observes state.snapshot roles reported in order "coder,architect"
