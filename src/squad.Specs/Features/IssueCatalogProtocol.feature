Feature: Issue catalog protocol

  squad-hq discovers Markdown issue files directly inside the fixed workspace "docs/issues" directory and returns
  them as an ordered, read-only catalog through the real "issues.list" UI protocol command - a dedicated request,
  independent of "state.snapshot", driven here through the real, separately launched "squad-hq --ui stdio" process
  and the real headless UI client. No step here reads the issue files or the catalog directly from disk; every
  observation crosses the real protocol.

  Background:
    Given a git project prepared with a "coder" role using the fake provider fixture
    When squad-hq is launched with "--ui stdio"
    And the ui sends "ui.ready"

  Scenario: A missing issue directory reports a successful, empty catalog
    When the ui requests the issue catalog with request id "req-missing"
    Then the issue catalog response for request id "req-missing" reports no issues

  Scenario: A present but empty issue directory reports a successful, empty catalog
    Given the issues directory exists and is empty
    When the ui requests the issue catalog with request id "req-empty"
    Then the issue catalog response for request id "req-empty" reports no issues

  Scenario: The catalog orders issues by ascending priority, breaks ties by filename, and lists unprioritized issues last
    Given an issue file "b-issue.md" with this content:
      """
      ---
      title: B issue
      priority: 5
      ---
      First body line.
      """
    And an issue file "a-issue.md" with this content:
      """
      ---
      title: A issue
      priority: 5
      ---
      Body line.
      """
    And an issue file "urgent.md" with this content:
      """
      ---
      title: Urgent issue
      priority: 1
      ---
      Body line.
      """
    And an issue file "invalid-priority.md" with this content:
      """
      ---
      title: Invalid priority issue
      priority: not-a-number
      ---
      Body line.
      """
    And an issue file "no-priority.md" with this content:
      """
      ---
      title: No priority issue
      ---
      Body line.
      """
    When the ui requests the issue catalog with request id "req-order"
    Then the issue catalog response for request id "req-order" reports issues in this order:
      | path                            | title                  | priority |
      | docs/issues/urgent.md           | Urgent issue           | 1        |
      | docs/issues/a-issue.md          | A issue                | 5        |
      | docs/issues/b-issue.md          | B issue                | 5        |
      | docs/issues/invalid-priority.md | Invalid priority issue |          |
      | docs/issues/no-priority.md      | No priority issue      |          |

  Scenario: The catalog includes only top-level Markdown files, matched case-insensitively, and excludes nested and non-Markdown siblings
    Given an issue file "included.md" with this content:
      """
      ---
      title: Included issue
      ---
      Body line.
      """
    And an issue file "CASED.MD" with this content:
      """
      ---
      title: Cased issue
      ---
      Body line.
      """
    And an issue file "notes.txt" with this content:
      """
      Not a markdown issue.
      """
    And an issue file "sub/nested.md" with this content:
      """
      ---
      title: Nested issue
      ---
      Body line.
      """
    When the ui requests the issue catalog with request id "req-exclusive"
    Then the issue catalog response for request id "req-exclusive" reports issues in this order:
      | path                    | title          | priority |
      | docs/issues/CASED.MD    | Cased issue    |          |
      | docs/issues/included.md | Included issue |          |

  Scenario: A document without a resolvable title falls back to its filename, independently of a valid priority
    Given an issue file "untitled.md" with this content:
      """
      ---
      priority: 3
      ---
      Body line.
      """
    When the ui requests the issue catalog with request id "req-fallback"
    Then the issue catalog response for request id "req-fallback" reports issues in this order:
      | path                    | title       | priority |
      | docs/issues/untitled.md | untitled.md | 3        |

  Scenario: A response reports the normalized raw frontmatter block and exactly five non-blank preview lines
    Given an issue file "detailed.md" with this content:
      """
      ---
      title: Detailed issue
      priority: 2
      ---
      First line.

      Second line.
      Third line.

      Fourth line.
      Fifth line.
      Sixth line, never previewed.
      """
    When the ui requests the issue catalog with request id "req-detail"
    Then the issue catalog response for request id "req-detail" includes an issue at path "docs/issues/detailed.md" with frontmatter:
      """
      ---
      title: Detailed issue
      priority: 2
      ---
      """
    And that issue reports these preview lines:
      | line          |
      | First line.   |
      | Second line.  |
      | Third line.   |
      | Fourth line.  |
      | Fifth line.   |

  Scenario: Malformed YAML frontmatter retains the issue with fallback title and priority instead of an error
    Given an issue file "malformed.md" with this content:
      """
      ---
      title: [unterminated
      priority: 4
      ---
      Body line.
      """
    When the ui requests the issue catalog with request id "req-malformed"
    Then the issue catalog response for request id "req-malformed" reports issues in this order:
      | path                     | title        | priority |
      | docs/issues/malformed.md | malformed.md |          |

  Scenario: An unclosed opening frontmatter delimiter retains the whole source as frontmatter and has no preview
    Given an issue file "unclosed.md" with this content:
      """
      ---
      title: Unclosed issue
      priority: 6
      Body line without a closing delimiter.
      """
    When the ui requests the issue catalog with request id "req-unclosed"
    Then the issue catalog response for request id "req-unclosed" reports issues in this order:
      | path                    | title       | priority |
      | docs/issues/unclosed.md | unclosed.md |          |
    And the issue catalog response for request id "req-unclosed" includes an issue at path "docs/issues/unclosed.md" with frontmatter:
      """
      ---
      title: Unclosed issue
      priority: 6
      Body line without a closing delimiter.
      """
    And that issue reports these preview lines:
      | line |

  Scenario: A document without any frontmatter delimiter has empty frontmatter and previews its first five non-blank lines
    Given an issue file "plain.md" with this content:
      """
      First line.

      Second line.
      Third line.

      Fourth line.
      Fifth line.
      Sixth line, never previewed.
      """
    When the ui requests the issue catalog with request id "req-plain"
    Then the issue catalog response for request id "req-plain" includes an issue at path "docs/issues/plain.md" with empty frontmatter
    And that issue reports these preview lines:
      | line         |
      | First line.  |
      | Second line. |
      | Third line.  |
      | Fourth line. |
      | Fifth line.  |

  Scenario: Re-requesting the catalog after files change during the same session reports the changed catalog
    Given an issue file "first.md" with this content:
      """
      ---
      title: First issue
      ---
      Body line.
      """
    When the ui requests the issue catalog with request id "req-before"
    Then the issue catalog response for request id "req-before" reports issues in this order:
      | path                 | title       | priority |
      | docs/issues/first.md | First issue |          |
    Given an issue file "second.md" with this content:
      """
      ---
      title: Second issue
      ---
      Body line.
      """
    When the ui requests the issue catalog with request id "req-after"
    Then the issue catalog response for request id "req-after" reports issues in this order:
      | path                  | title        | priority |
      | docs/issues/first.md  | First issue  |          |
      | docs/issues/second.md | Second issue |          |

  Scenario: A genuine filesystem failure reports a correlated protocol error instead of a misleading empty catalog
    Given the issues directory is replaced with a plain file
    When the ui requests the issue catalog with request id "req-broken"
    Then a correlated protocol error for request id "req-broken" is reported
