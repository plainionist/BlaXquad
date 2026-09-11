Feature: Transcript oversized content

  Production per-entry and per-announcement limits bound transcript memory and Headquarters-control notification
  content even when a single message or a continuous stream vastly exceeds them, and the archive independently
  preserves
  as much of that same content as its own, much larger per-entry storage bound allows - reporting truncation
  explicitly rather than silently presenting partial content as complete. These scenarios drive the published
  squad-hq process with the fake provider and observe only the real protocol, never production transcript
  storage directly.

  Background:
    Given `blaxquad/squad.json` configures:
      | role  |
      | coder |
    When the operator launches Headquarters
    Then Headquarters starts an agent session for role "coder"

  Scenario: An oversized transcript entry remains bounded in memory while its full content stays available in the archive
    When the "coder" agent emits a system message with 300000 characters
    Then the transcript update for role "coder" reports archived content beyond the 250000 character retained bound
    When the UI-protocol client requests the archived transcript entry 2 for role "coder"
    Then the archived transcript entry has 300000 characters and is not truncated

  Scenario: An oversized transcript announcement is explicitly reported as truncated
    When the "coder" agent emits a system message with 20000 characters
    Then the transcript update for role "coder" reports a truncated announcement of 16384 characters

  Scenario: Archived streaming content beyond the storage limit is explicitly reported as truncated
    When the "coder" agent emits an assistant delta with 1200000 characters
    And the "coder" agent emits an assistant delta with 1200000 characters
    And the UI-protocol client requests the archived transcript entry 2 for role "coder"
    Then the archived transcript entry is truncated with 2400000 total characters at the 2000000 character archive bound

  Scenario: An entry that has rotated out of the archive is reported as unavailable while newer archived entries remain available
    # Entry 0 is the automatic "Session started." system entry and entry 1 is the automatic initial-instruction
    # harness entry every fresh role session publishes first, so this burst of system messages, each sized at the
    # production per-entry archive bound, itself occupies entries 2 through 16. Their combined 30,000,000 archived
    # characters push this role's archive well past its 20,000,000 character total bound, so the archive evicts
    # entries oldest-first - entries 0, 1, then this burst's own oldest entries - until the remaining total again
    # fits, landing back at exactly the bound with entries 7 through 16 left standing. Entry 2, this burst's own
    # oldest message, is therefore always among those rotated out, while entry 16, its newest, always survives.
    #
    # Live retention, meanwhile, always live-truncates each of these oversized messages down to the 250,000
    # character per-entry retained bound, so the burst's combined retained size (3,750,000 characters) also
    # crosses the 1,000,000 character live retention bound - live retention likewise evicts oldest-first until only
    # the newest 4 entries (13 through 16) remain, exactly filling that bound. Paging back from that live boundary
    # must therefore surface exactly the remaining archived entries older than it (7 through 12) and report no
    # further history, since the rotated-out entries below 7 are gone from the archive entirely.
    When the "coder" agent emits 15 system messages with 2000000 characters each
    And the user requests a fresh transcript synchronization for role "coder"
    Then the transcript synchronization for role "coder" contains exactly 4 entries
    When the UI-protocol client requests the archived transcript entry 2 for role "coder"
    Then the archived transcript entry is unavailable for role "coder"
    When the UI-protocol client requests the archived transcript entry 16 for role "coder"
    Then the archived transcript entry has 2000000 characters and is not truncated
    When the UI-protocol client requests the previous transcript page for role "coder"
    Then the previous transcript page for role "coder" contains exactly 6 entries
    And the previous transcript page for role "coder" reports no more history

  Scenario: Synchronization reports a still-live entry's rotated archive content as no longer available
    # An assistant delta that is never finalized keeps streaming - its retained entry stays pinned in live
    # retention indefinitely, exempt from the ordinary oldest-first live eviction that would otherwise apply once
    # enough later entries accumulate - while the archive holds no such exemption and evicts its backing content
    # oldest-first exactly like any other entry. Publishing enough later history therefore rotates this entry's
    # own archived content out from under it despite it staying live the entire time, and a synchronization taken
    # afterward must report that reversal explicitly rather than continuing to claim the earlier content is still
    # available in transcript history.
    When the "coder" agent emits an assistant delta with 260000 characters
    And the "coder" agent emits 15 system messages with 2000000 characters each
    And the user requests a fresh transcript synchronization for role "coder"
    Then the transcript synchronization for role "coder" reports "assistant" content that is no longer available
