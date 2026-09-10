Feature: Transcript file-read summaries without content leakage

  A file-read tool call can be started with only a path, with an explicit line range, or as a whole-file read whose
  extent is only known once it completes. Every one of these must show what was read - the path, the requested
  range, or the whole file's derived line count - while never publishing the file's contents or a completion's own
  detailed output through the transcript protocol. These scenarios drive the published squad-hq process with the
  fake provider and observe only the real protocol through the headless UI client and the fake-provider control
  pipe.

  Scenario: Path-only file reads show their path without leaking file contents
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    When the "coder" agent starts tool call "R1" named "read_file" for path "src/App.cs"
    Then the backend scenario observes a transcript update for role "coder" with source "read" and content "src/App.cs"
    When the "coder" agent starts tool call "R2" named "view_file" for path "src/Main.cs"
    Then the backend scenario observes a transcript update for role "coder" with source "read" and content "src/Main.cs"
    When the backend scenario requests a fresh transcript synchronization
    Then the transcript synchronization for role "coder" includes exactly these entries:
      | source | content     |
      | read   | src/App.cs  |
      | read   | src/Main.cs |
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero

  Scenario: A ranged file read shows the requested range without leaking file contents or completion payload
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    When the "coder" agent starts tool call "R" named "view" with arguments:
      """
      {"path":"C:\\work\\src\\Main.cs","view_range":[100,1000]}
      """
    Then the backend scenario observes a transcript update for role "coder" with source "read"
    When the "coder" agent emits tool output "file contents" for tool call "R"
    And the "coder" agent completes tool call "R" named "view" with detailed output "diff --git"
    And the "coder" agent starts tool call "B" named "dotnet build"
    And the "coder" agent emits tool output "Build succeeded" for tool call "B"
    Then the backend scenario observes a transcript update for role "coder" with source "tool" and content "dotnet build\nBuild succeeded"
    When the backend scenario requests a fresh transcript synchronization
    # A single literal backslash in the actual path requires two literal backslash characters in this data-table
    # cell - Reqnroll's own table parsing unescapes "\\" to "\" before this step sees the cell, exactly as the
    # docstring above needs "\\" to embed one real backslash in the JSON text production echoes back.
    Then the transcript synchronization for role "coder" includes exactly these entries:
      | source | content                            |
      | read   | C:\\work\\src\\Main.cs [100..1000] |
      | tool   | dotnet build\nBuild succeeded       |
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero

  Scenario: A whole-file read shows its derived line count without leaking file contents
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    When the "coder" agent starts tool call "R" named "view" for path "C:\work\src\Main.cs"
    Then the backend scenario observes a transcript update for role "coder" with source "read"
    When the "coder" agent completes tool call "R" named "view" with display output "diff --git" and content "first\nsecond\nthird"
    Then the reconciled transcript for role "coder" contains each of these entries exactly once:
      | source | content                     |
      | read   | C:\\work\\src\\Main.cs [1..3] |
    And the backend scenario does not observe the transcript for role "coder" containing "first" within 2 seconds
    And the backend scenario does not observe the transcript for role "coder" containing "diff --git" within 2 seconds
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero
