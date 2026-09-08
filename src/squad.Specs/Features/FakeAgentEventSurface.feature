Feature: The fake-agent event surface covers the remaining event families the documented backend test API supports

  Slices 6-9 proved the fake provider's minimal session lifecycle and its prompt/reply round trip. This slice
  extends the same private control pipe and semantic role controller with every remaining event family the
  documented backend test API supports: harness messages, aborts, interaction responses, reasoning/tool output,
  readiness, usage, interaction requests, idle, operation/session completion, and failures. Each family is proven
  through a typed semantic operation on the role controller (or the real UI protocol, for host-authored commands),
  a deterministic acknowledgement or bounded wait, and role/session routing - the same black-box discipline as
  every other scenario in this suite, with no product test hook and no arbitrary sleep.

  Scenario: The role's session sends its initial harness instruction when the session starts, and it appears in the real transcript
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the "coder" agent observes a harness message
    And the observed harness message appears in the transcript for role "coder"
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero

  Scenario: An abort sent through the real UI protocol is observed through the role controller
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    When the backend scenario requests an abort for role "coder"
    Then the "coder" agent observes an abort
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero

  Scenario: Permission, input, and elicitation requests are observed in the transcript and answered through the real UI protocol
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    When the "coder" agent requests permission "perm-1" with description "Run the deploy script?"
    Then the backend scenario observes the transcript for role "coder" containing "Permission required: Run the deploy script?."
    When the backend scenario responds to permission "perm-1" for role "coder" with approved "true"
    Then the "coder" agent observes a permission response for "perm-1" approved "true"
    When the "coder" agent requests input "input-1" with prompt "Which branch should I use?"
    Then the backend scenario observes the transcript for role "coder" containing "Which branch should I use?"
    When the backend scenario responds to input "input-1" for role "coder" with answer "main"
    Then the "coder" agent observes an input response for "input-1" with answer "main"
    When the "coder" agent requests elicitation "elic-1" with prompt "Confirm the deployment?" and mode "confirm"
    Then the backend scenario observes the transcript for role "coder" containing "Confirm the deployment?"
    When the backend scenario responds to elicitation "elic-1" for role "coder" with action "accept"
    Then the "coder" agent observes an elicitation response for "elic-1" with action "accept"
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero

  Scenario: Reasoning is observed in the real transcript, tool output round-trips through acknowledged emits, and idle is observed as a status
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    When the "coder" agent emits the reasoning "Considering which tests to run."
    Then the backend scenario observes the transcript for role "coder" containing "Considering which tests to run."
    When the "coder" agent emits a full tool lifecycle for tool call "tool-1" named "run_tests"
    When the "coder" agent emits idle
    Then the backend scenario observes role "coder" at status "idle"
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero

  Scenario: Readiness is observed as a role status and usage is observed as a role snapshot field
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    When the "coder" agent emits readiness "busy"
    Then the backend scenario observes role "coder" at status "busy"
    When the "coder" agent emits usage "2.5"
    Then the backend scenario observes role "coder" at AI-credit usage "2.5"
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero

  Scenario: A session that completes gracefully is observed as the role reaching status stopped
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    When the "coder" agent completes its session
    Then the backend scenario observes role "coder" at status "stopped"
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero

  Scenario: A session that fails is observed as the role reaching status error
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    When the "coder" agent fails its session with message "The tool crashed."
    Then the backend scenario observes role "coder" at status "error"
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero
