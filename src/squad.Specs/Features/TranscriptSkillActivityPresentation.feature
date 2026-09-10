Feature: Transcript skill activity presentation

  Applying a skill publishes one semantic "using skill(name)" activity entry directly - independent of the
  skill-loading tool call underneath it, whose own start, streamed SKILL.md contents, and completion must never
  reach the transcript. Skill discovery remains a plain, visible system message, distinguishable from a file read
  of the same path. These scenarios drive the published squad-hq process with the fake provider and observe only
  the real protocol through the headless UI client and the fake-provider control pipe.

  Background:
    Given `blaxquad/squad.json` configures:
      | role  |
      | coder |
    When the operator launches Headquarters
    Then Headquarters starts an agent session for role "coder"

  Scenario: Skill application is visible in the transcript
    When the "coder" agent invokes skill "project-setup"
    Then the dashboard receives a transcript update for role "coder" with source "tool" and content "using skill(project-setup)"
    When the operator shuts down Headquarters
    Then Headquarters exits with code 0

  Scenario: Skill tool plumbing is omitted from the transcript
    When the "coder" agent starts tool call "S" named "skill" with arguments:
      """
      {"skill":"project-setup"}
      """
    And the "coder" agent emits tool output "full SKILL.md contents" for tool call "S"
    And the "coder" agent completes tool call "S" named "skill"
    Then the dashboard shows role "coder" with no active tool
    When the "coder" agent starts tool call "B" named "dotnet build"
    Then the dashboard receives a transcript update for role "coder" with source "tool" and content "dotnet build"
    When the user requests a fresh transcript synchronization for role "coder"
    Then the transcript synchronization for role "coder" includes exactly these entries:
      | source | content      |
      | tool   | dotnet build |
    When the operator shuts down Headquarters
    Then Headquarters exits with code 0

  Scenario: Skill discovery remains visible while distinguished from file reads
    When the "coder" agent emits a system message "Discovered skill: C:\skills\analyze-issue\SKILL.md"
    Then the dashboard receives a transcript update for role "coder" with source "system" and content "Discovered skill: C:\skills\analyze-issue\SKILL.md"
    And the transcript for role "coder" does not contain "Read C:\skills\analyze-issue\SKILL.md" within 2 seconds
    When the operator shuts down Headquarters
    Then Headquarters exits with code 0
