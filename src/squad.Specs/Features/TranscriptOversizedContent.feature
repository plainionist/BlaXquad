Feature: Transcript oversized content

  Production per-entry and per-announcement limits bound transcript memory and host-control notification content
  even when a single message or a continuous stream vastly exceeds them, and the archive independently preserves
  as much of that same content as its own, much larger per-entry storage bound allows - reporting truncation
  explicitly rather than silently presenting partial content as complete. These scenarios drive the published
  squad-hq process with the fake provider and observe only the real protocol, never production transcript
  storage directly.

  Background:
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe

  Scenario: An oversized transcript entry remains bounded in memory while its full content stays available in the archive
    When the "coder" agent emits a system message with 300000 characters
    Then the transcript update for role "coder" reports archived content beyond the retained bound
    When the backend scenario requests the archived transcript entry 2 for role "coder"
    Then the archived transcript entry has 300000 characters and is not truncated

  Scenario: An oversized transcript announcement is explicitly reported as truncated
    When the "coder" agent emits a system message with 20000 characters
    Then the transcript update for role "coder" reports a truncated announcement of 16384 characters

  Scenario: Archived streaming content beyond the storage limit is explicitly reported as truncated
    When the "coder" agent emits an assistant delta with 1200000 characters
    And the "coder" agent emits an assistant delta with 1200000 characters
    And the backend scenario requests the archived transcript entry 2 for role "coder"
    Then the archived transcript entry is truncated with 2400000 total characters
