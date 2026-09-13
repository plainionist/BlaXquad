Feature: Transcript subagent activity presentation

  Starting a subagent can carry its full metadata - an internal agent name, a human-authored task description, and
  the model it runs - or only some of that: a matching description that adds nothing beyond the agent's own name,
  no model, or no metadata at all. Every one of these must still show one meaningful subagent activity entry using
  the best available label, while the subagent's own control-plane tool calls (delegating to it, inspecting it, or
  listing active subagents) must never appear as ordinary tool entries. These scenarios drive the published
  squad-hq process with the fake provider and observe only the real protocol through the headless UI client and the
  fake-provider control pipe.

  Background:

    Given `blaxquad/squad.json` configures:
      | role  |
      | coder |

    When the operator launches Headquarters

    Then Headquarters starts an agent session for role "coder"

  Scenario: Subagent activity shows the best available label across supported metadata fallbacks

    When the "coder" agent starts subagent "code-review" displayed as "Review authentication changes" using model "gpt-5.6-sol"

    Then the dashboard receives a transcript update for role "coder" with source "subagent" and content "Code Review · gpt-5.6-sol · Review authentication changes"

    When the "coder" agent starts subagent "explore" displayed as "Explore" using model "claude-sonnet"

    Then the dashboard receives a transcript update for role "coder" with source "subagent" and content "Explore · claude-sonnet"

    When the "coder" agent starts subagent "explore" displayed as "Find authentication entries" using model ""

    Then the dashboard receives a transcript update for role "coder" with source "subagent" and content "Explore · Find authentication entries"

    When the "coder" agent starts subagent "" displayed as "" using model ""

    Then the dashboard receives a transcript update for role "coder" with source "subagent" and content "Subagent"

    When the operator shuts down Headquarters

    Then Headquarters exits with code 0

  Scenario: Subagent control-plane tool calls never appear as ordinary tool entries

    When the "coder" agent starts tool call "T" named "task"
    And the "coder" agent starts tool call "R" named "read_agent"
    And the "coder" agent starts tool call "L" named "list_agents"

    Then the dashboard shows role "coder" with no active tool

    When the "coder" agent starts tool call "B" named "dotnet build"

    Then the dashboard receives a transcript update for role "coder" with source "tool" and content "dotnet build"

    When the user requests a fresh transcript synchronization for role "coder"

    Then the transcript synchronization for role "coder" includes exactly these entries:
      | source | content      |
      | tool   | dotnet build |

    When the operator shuts down Headquarters

    Then Headquarters exits with code 0
