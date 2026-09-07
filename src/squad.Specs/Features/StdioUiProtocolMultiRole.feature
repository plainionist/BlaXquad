Feature: Stdio UI protocol output under concurrent multi-role activity
  Two roles observing and replying independently exercise the same stdout write path at once. Every accepted
  envelope must still be one complete, well-formed, flushed JSON line with no torn or interleaved framing.

  Background:
    Given a git project configured with "coder" and "reviewer" roles using the echo provider fixture

  Scenario: Rapid interleaved prompts across two roles never corrupt stdout framing
    When squad-hq is launched with "--ui stdio"
    And the ui sends "ui.ready"
    Then a "transcript.update" message for role "coder" with content "Session started." is written to stdout
    And a "transcript.update" message for role "reviewer" with content "Session started." is written to stdout
    When the ui sends a "prompt.send" command for role "coder" with prompt "hello-coder-1"
    And the ui sends a "prompt.send" command for role "reviewer" with prompt "hello-reviewer-1"
    And the ui sends a "prompt.send" command for role "coder" with prompt "hello-coder-2"
    And the ui sends a "prompt.send" command for role "reviewer" with prompt "hello-reviewer-2"
    Then a "transcript.update" message for role "coder" with content "echo: hello-coder-2" is written to stdout
    And a "transcript.update" message for role "reviewer" with content "echo: hello-reviewer-2" is written to stdout
    And every stdout line is a well-formed protocol envelope
