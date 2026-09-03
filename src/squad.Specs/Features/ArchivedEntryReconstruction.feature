Feature: Archived transcript entry reconstruction
  The backend supplies authoritative archive and retention offsets for browser reconstruction.

  Scenario Outline: Archived entries report exact reconstruction inputs
    Given a ViewModel retaining <retained limit> characters per entry and archiving <archive limit> characters per entry
    When the recording "coder" session emits a <total characters> character patterned user message
    Then archived reconstruction inputs for "coder" entry 0 are:
      | sequence | retained offset   | retained characters   | archive available   | content truncated   | total characters   | archived prefix   | archived characters   |
      | 1        | <retained offset> | <retained characters> | <archive available> | <content truncated> | <total characters> | <archived prefix> | <archived characters> |

    Examples:
      | retained limit | archive limit | total characters | retained offset | retained characters | archive available | content truncated | archived prefix | archived characters |
      | 100            | 300           | 210              | 164             | 100                 | true              | false             | 210             | 210                 |
      | 150            | 200           | 210              | 114             | 150                 | true              | true              | 136             | 200                 |
      | 100            | 120           | 210              | 164             | 100                 | true              | true              | 56              | 120                 |
      | 50             | 120           | 210              | 210             | 50                  | true              | true              | 56              | 120                 |
      | 50             | 60            | 210              | 210             | 42                  | false             | true              | 0               | 60                  |

  Scenario: An entry evicted from live retention remains available in the archive
    Given a ViewModel retaining 2 entries and archiving 3 entries
    When the recording "coder" session emits 3 user messages
    Then live transcript for "coder" excludes entry 0
    And archived entry 0 for "coder" has sequence 3 and content "message-0"

  Scenario: Archive rotation returns an unavailable entry at the current sequence
    Given a ViewModel retaining 2 entries and archiving 3 entries
    When the recording "coder" session emits 6 user messages
    Then live transcript for "coder" excludes entry 0
    And unavailable archived entry 0 for "coder" has sequence 6
