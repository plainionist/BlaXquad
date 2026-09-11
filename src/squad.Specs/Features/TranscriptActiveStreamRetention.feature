Feature: Transcript active stream retention

  An active assistant or reasoning stream's entry is excluded from live-retention eviction while it is still being
  appended to, so unrelated transcript activity that crosses the production live-retention boundary must never
  split, lose, or misroute its later delta into a different entry. These scenarios drive the published squad-hq
  process with the fake provider and observe only the real protocol, never production transcript storage.

  Background:
    Given `blaxquad/squad.json` configures:
      | role  |
      | coder |
    When the operator launches Headquarters
    Then Headquarters starts an agent session for role "coder"

  Scenario: An active assistant stream keeps receiving its later delta after unrelated activity crosses the live-retention boundary
    When the "coder" agent emits an assistant delta "Hello "
    Then the dashboard receives a transcript update for role "coder" with source "assistant" and content "Hello "
    When the "coder" agent emits 5 system messages with 250000 characters each
    And the "coder" agent emits an assistant delta "world"
    Then the dashboard receives a transcript update for role "coder" with operation "append-content" and content "world"
    And the most recently received transcript updates for role "coder" report the same entry index
    When the user requests a fresh transcript synchronization for role "coder"
    Then the transcript synchronization for role "coder" includes an entry with source "assistant" and content "Hello world"

  Scenario: An active reasoning stream keeps receiving its later delta after unrelated activity crosses the live-retention boundary
    When the "coder" agent emits a reasoning delta "draft "
    Then the dashboard receives a transcript update for role "coder" with source "reasoning" and content "draft "
    When the "coder" agent emits 5 system messages with 250000 characters each
    And the "coder" agent emits a reasoning delta "final"
    Then the dashboard receives a transcript update for role "coder" with operation "append-content" and content "final"
    And the most recently received transcript updates for role "coder" report the same entry index
    When the user requests a fresh transcript synchronization for role "coder"
    Then the transcript synchronization for role "coder" includes an entry with source "reasoning" and content "draft final"
