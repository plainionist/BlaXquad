Feature: Snapshot publication
  Photino publishes current UI state without overlapping expensive snapshots.

  Scenario: Refresh bursts are coalesced through one publisher
    Given a slow snapshot publisher
    When an immediate snapshot starts
    And deferred and immediate refreshes arrive during publication
    And the first snapshot is allowed to finish
    Then snapshot publication concurrency never exceeds one
    And exactly one follow-up snapshot contains the latest state
    And disposing the snapshot publisher stops further publication

  Scenario: Immediate refresh bypasses a deferred publication delay
    Given a snapshot publisher with a long deferred interval
    When a deferred snapshot is requested
    Then no snapshot is published during the deferred interval
    When an immediate snapshot is requested
    Then one snapshot is published without the deferred delay

  Scenario: Disposal waits for active publication and drops queued work
    Given a slow snapshot publisher
    When an immediate snapshot starts
    And a follow-up refresh is queued
    And snapshot publisher disposal starts
    Then snapshot publisher disposal is waiting
    When the active snapshot is allowed to finish
    Then snapshot publisher disposal completes
    And no follow-up snapshot is published
