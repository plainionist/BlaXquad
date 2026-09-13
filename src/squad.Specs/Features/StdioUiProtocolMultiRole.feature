Feature: Stdio UI protocol output under concurrent multi-role activity

  Two roles observing and replying independently exercise the same stdout write path at once. Every accepted
  envelope must still be one complete, well-formed, flushed JSON line with no torn or interleaved framing.

  Background:

    Given `blaxquad/squad.json` configures:
      | role     |
      | coder    |
      | reviewer |

    When the operator launches Headquarters with the "stdio" UI transport

  Scenario: Rapid interleaved prompts across two roles never corrupt stdout framing

    When a UI-protocol client sends "ui.ready"

    Then a "transcript.update" message for role "coder" with content "Session started." is written to stdout
    And a "transcript.update" message for role "reviewer" with content "Session started." is written to stdout

    When a UI-protocol client sends a "prompt.send" command for role "coder" with prompt "hello-coder-1"
    And a UI-protocol client sends a "prompt.send" command for role "reviewer" with prompt "hello-reviewer-1"
    And a UI-protocol client sends a "prompt.send" command for role "coder" with prompt "hello-coder-2"
    And a UI-protocol client sends a "prompt.send" command for role "reviewer" with prompt "hello-reviewer-2"

    Then a "transcript.update" message for role "coder" with content "echo: hello-coder-2" is written to stdout
    And a "transcript.update" message for role "reviewer" with content "echo: hello-reviewer-2" is written to stdout
    And every stdout line is a well-formed protocol envelope
