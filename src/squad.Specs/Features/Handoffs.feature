Feature: Creating outbound handoffs
  Agents queue durable handoffs directly through `squad handoff commit`/`squad handoff note`, with no
  intermediate draft file.

  Background:
    Given `blaxquad/squad.json` configures:
      | role      |
      | coder     |
      | reviewer  |
      | architect |

  Scenario: Queue a Git handoff for HEAD with the default priority
    Given "coder" has a committed change
    And "coder" prepares a Git handoff with task "implement-search" to:
      | role     |
      | reviewer |
    When the "coder" role agent runs `squad handoff` from its worktree
    Then the command succeeds
    And one handoff is queued
    And the queued handoff was sent by "coder" to:
      | role     |
      | reviewer |
    And the queued handoff has priority "50"
    And the queued handoff is a Git handoff for task "implement-search"
    And the queued handoff instructs merging the committed change

  Scenario: Queue a Git handoff for an explicit revision and an overridden priority
    Given "coder" has a committed change
    And "coder" prepares a Git handoff with priority "20" and task "implement-search" for revision "squad-coder" to:
      | role     |
      | reviewer |
    When the "coder" role agent runs `squad handoff` from its worktree
    Then the command succeeds
    And one handoff is queued
    And the queued handoff has priority "20"
    And the queued handoff is a Git handoff for task "implement-search"
    And the queued handoff instructs merging the committed change

  Scenario: Queue a note for several recipients
    Given "coder" prepares a note with priority "70" and message "Please inspect the delivery result." to:
      | role      |
      | reviewer  |
      | architect |
    When the "coder" role agent runs `squad handoff` from its worktree
    Then the command succeeds
    And one handoff is queued
    And the queued handoff was sent by "coder" to:
      | role      |
      | reviewer  |
      | architect |
    And the queued handoff is a note with message "Please inspect the delivery result."

  Scenario: Reject a default HEAD handoff from a dirty worktree, then accept an explicit revision
    Given "coder" has a committed change
    And "coder" has an uncommitted change
    And "coder" prepares a Git handoff with task "implement-search" to:
      | role     |
      | reviewer |
    When the "coder" role agent runs `squad handoff` from its worktree
    Then the command exits with code 2
    And standard error contains "uncommitted changes"
    And no handoff is queued
    When "coder" prepares a Git handoff with priority "20" and task "implement-search" for revision "squad-coder" to:
      | role     |
      | reviewer |
    And the "coder" role agent runs `squad handoff` from its worktree
    Then the command succeeds
    And one handoff is queued
    And the queued handoff instructs merging the committed change

  Scenario: Reject simultaneous priority and recipient errors in a note
    When "coder" runs `squad handoff` with arguments:
      | arg                                  |
      | note                                 |
      | --to                                 |
      | reviewer,missing,reviewer            |
      | --priority                           |
      | urgent                               |
      | --message                            |
      | Please inspect the delivery result.  |
    Then the command exits with code 2
    And standard error contains "priority"
    And standard error contains "Unknown recipient role 'missing'"
    And standard error contains "Duplicate recipient 'reviewer'"
    And no handoff is queued

  Scenario: Reject option-shape errors in a direct handoff command
    When "coder" runs `squad handoff` with arguments:
      | arg               |
      | commit            |
      | --to              |
      | reviewer          |
      | --task            |
      | implement-search  |
      | --task            |
      | implement-search2 |
      | --unknown         |
      | value             |
      | extra             |
    Then the command exits with code 2
    And standard error contains "Duplicate option '--task'"
    And standard error contains "Option '--unknown' is not valid for 'commit'"
    And standard error contains "Unexpected argument 'extra'"
    And no handoff is queued

  Scenario: Reject a task name longer than 80 characters
    When "coder" runs `squad handoff` with arguments:
      | arg                                                                                 |
      | commit                                                                              |
      | --to                                                                                |
      | reviewer                                                                            |
      | --task                                                                              |
      | this-task-name-is-deliberately-far-too-long-to-be-accepted-as-a-stable-task-name-abc |
    Then the command exits with code 2
    And standard error contains "no longer than 80 characters"
    And no handoff is queued

  Scenario: Reject a commit option that does not resolve to a Git commit
    When "coder" runs `squad handoff` with arguments:
      | arg               |
      | commit            |
      | --to              |
      | reviewer          |
      | --task            |
      | implement-search  |
      | --commit          |
      | not-a-revision    |
    Then the command exits with code 2
    And standard error contains "must resolve to exactly one Git commit"
    And no handoff is queued

  Scenario: Show the direct command forms without a draft file format
    When "coder" runs `squad handoff` with arguments:
      | arg    |
      | --help |
    Then the command succeeds
    And standard output contains "handoff commit --to"
    And standard output contains "handoff note --to"
