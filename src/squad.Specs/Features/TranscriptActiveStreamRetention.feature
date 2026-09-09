Feature: Transcript active stream retention

  An active assistant or reasoning stream's entry is excluded from live-retention eviction while it is still being
  appended to, so unrelated transcript activity that crosses the production live-retention boundary must never
  split, lose, or misroute its later delta into a different entry. These scenarios drive the published squad-hq
  process with the fake provider and observe only the real protocol, never production transcript storage.

  Background:
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe

  Scenario: An active assistant stream keeps receiving its later delta after unrelated activity crosses the live-retention boundary
    When the "coder" agent emits an assistant delta "Hello "
    Then the backend scenario observes a transcript update for role "coder" with source "assistant" and content "Hello "
    When the "coder" agent emits a system message with 250000 characters
    And the "coder" agent emits a system message with 250000 characters
    And the "coder" agent emits a system message with 250000 characters
    And the "coder" agent emits a system message with 250000 characters
    And the "coder" agent emits a system message with 250000 characters
    And the "coder" agent emits an assistant delta "world"
    Then the backend scenario observes a transcript update for role "coder" with operation "append-content" and content "world"
    And the most recently observed transcript updates for role "coder" report the same entry index
    When the backend scenario requests a fresh transcript synchronization
    Then the transcript synchronization for role "coder" includes an entry with source "assistant" and content "Hello world"

  Scenario: An active reasoning stream keeps receiving its later delta after unrelated activity crosses the live-retention boundary
    When the "coder" agent emits a reasoning delta "draft "
    Then the backend scenario observes a transcript update for role "coder" with source "reasoning" and content "draft "
    When the "coder" agent emits a system message with 250000 characters
    And the "coder" agent emits a system message with 250000 characters
    And the "coder" agent emits a system message with 250000 characters
    And the "coder" agent emits a system message with 250000 characters
    And the "coder" agent emits a system message with 250000 characters
    And the "coder" agent emits a reasoning delta "final"
    Then the backend scenario observes a transcript update for role "coder" with operation "append-content" and content "final"
    And the most recently observed transcript updates for role "coder" report the same entry index
    When the backend scenario requests a fresh transcript synchronization
    Then the transcript synchronization for role "coder" includes an entry with source "reasoning" and content "draft final"
