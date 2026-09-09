Feature: Transcript history paging

  A reconnecting dashboard rebuilds a role's entire still-available transcript from two operations: a bounded live
  synchronization for its most recent history, and however many "previous transcript page" requests it takes to
  page all the way back to the very first entry. Neither operation may ever expose the request envelope's raw
  index coordinates or the on-disk archive's storage location to the dashboard itself - and combining every page
  with the live synchronization must never introduce a gap or a duplicate, even once enough activity has crossed
  both the production live-retention boundary and the protocol's own page-size boundary. These scenarios drive the
  published squad-hq process with the fake provider and observe only the real protocol, never production
  transcript storage directly.

  Background:
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    And the backend scenario isolates its temporary transcript directory
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe

  Scenario: Live synchronization stays bounded and every older entry pages back without gaps or duplicates
    When the "coder" agent emits 700 system messages
    And the backend scenario requests a fresh transcript synchronization
    Then the transcript synchronization for role "coder" contains exactly 500 entries
    When the backend scenario requests the previous transcript page for role "coder"
    Then the previous transcript page for role "coder" contains exactly 200 entries
    And the previous transcript page for role "coder" reports more history
    When the backend scenario requests the previous transcript page for role "coder"
    Then the previous transcript page for role "coder" reports no more history
    And the combined transcript history observed for role "coder" contains message 0 through message 699 exactly once

  Scenario: An entry evicted from live retention remains available in the archive
    When the "coder" agent emits 700 system messages
    And the backend scenario requests the archived transcript entry 0 for role "coder"
    Then the archived transcript entry has content "Session started."

  Scenario: Temporary transcript history is removed once the process shuts down cleanly
    When the "coder" agent emits a system message "seed"
    Then the backend scenario's temporary transcript history exists
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero
    And the backend scenario's temporary transcript history no longer exists
