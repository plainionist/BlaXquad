Feature: Stdio transport concurrent command dispatch and shutdown draining

  The stdio host's input pump owns line framing and admission only: it dispatches each received command without
  waiting for that command's own domain operation to complete before reading the next line, and it drains every
  dispatched command before disposing the protocol session on shutdown - even one still in flight when shutdown
  is requested.

  Scenario: A host-control shutdown drains a still in-flight prompt dispatch instead of hanging or crashing
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    When the backend scenario sends the prompt "hello" to role "coder"
    Then the "coder" agent observes the prompt "hello"
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero
