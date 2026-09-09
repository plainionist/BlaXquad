Feature: Transcript skill activity presentation

  Applying a skill publishes one semantic "using skill(name)" activity entry directly - independent of the
  skill-loading tool call underneath it, whose own start, streamed SKILL.md contents, and completion must never
  reach the transcript. Skill discovery remains a plain, visible system message, distinguishable from a file read
  of the same path. These scenarios drive the published squad-hq process with the fake provider and observe only
  the real protocol through the headless UI client and the fake-provider control pipe.

  Scenario: Skill application is visible in the transcript
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    When the "coder" agent invokes skill "project-setup"
    Then the backend scenario observes a transcript update for role "coder" with source "tool" and content "using skill(project-setup)"
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero

  Scenario: Skill tool plumbing is omitted from the transcript
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    When the "coder" agent starts tool call "S" named "skill" with arguments:
      """
      {"skill":"project-setup"}
      """
    And the "coder" agent emits partial tool output "full SKILL.md contents" for tool call "S"
    And the "coder" agent completes tool call "S" named "skill"
    Then the backend scenario observes role "coder" with no active tool
    When the "coder" agent starts tool call "B" named "dotnet build"
    Then the backend scenario observes a transcript update for role "coder" with source "tool" and content "dotnet build"
    When the backend scenario requests a fresh transcript synchronization
    Then the transcript synchronization for role "coder" includes exactly these entries:
      | source | content      |
      | tool   | dotnet build |
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero

  Scenario: Skill discovery remains visible while distinguished from file reads
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    When the "coder" agent emits a system message "Discovered skill: C:\skills\analyze-issue\SKILL.md"
    Then the backend scenario observes a transcript update for role "coder" with source "system" and content "Discovered skill: C:\skills\analyze-issue\SKILL.md"
    And the backend scenario does not observe the transcript for role "coder" containing "Read C:\skills\analyze-issue\SKILL.md" within 2 seconds
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero
