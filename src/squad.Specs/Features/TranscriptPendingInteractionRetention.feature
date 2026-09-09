Feature: Transcript pending interaction retention

  A pending permission request registers a protected "harness" transcript entry so it can still be described to a
  reconnecting dashboard. That protection must keep the entry live even after enough later transcript activity
  crosses the production live-retention boundary, which would otherwise evict it as the oldest unprotected entry -
  and the interaction must still remain answerable afterward. These scenarios drive the published squad-hq process
  with the fake provider and observe only the real protocol, never production transcript storage.

  Background:
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe

  Scenario: A pending permission's transcript context survives crossing the live-retention boundary
    When the "coder" agent requests permission "permission-1" with description "Run the deploy script?"
    Then the backend scenario observes a pending permission "permission-1" for role "coder" with description "Run the deploy script?"
    When the "coder" agent emits a system message with 250000 characters
    And the "coder" agent emits a system message with 250000 characters
    And the "coder" agent emits a system message with 250000 characters
    And the "coder" agent emits a system message with 250000 characters
    And the "coder" agent emits a system message with 250000 characters
    Then the backend scenario observes a pending permission "permission-1" for role "coder" with description "Run the deploy script?"
    When the backend scenario requests a fresh transcript synchronization
    Then the transcript synchronization for role "coder" includes an entry with source "harness" and content "Permission required: Run the deploy script?."
    When the backend scenario responds to permission "permission-1" for role "coder" with approved "true"
    Then the "coder" agent observes a permission response for "permission-1" approved "true"
