Feature: Stdio transport concurrent command dispatch and shutdown draining

  The stdio host's input pump owns line framing and admission only: it dispatches each received command without
  waiting for that command's own domain operation to complete before reading the next line, and it drains every
  dispatched command before disposing the protocol session on shutdown - even one still in flight when shutdown
  is requested.

  Scenario: A host-control shutdown drains a still in-flight prompt dispatch instead of hanging or crashing
    Given `blaxquad/squad.json` configures:
      | role  |
      | coder |
    When the operator launches Headquarters
    Then Headquarters starts an agent session for role "coder"
    When the user sends "hello" to role "coder"
    Then the "coder" agent observes the prompt "hello"
    When the operator shuts down Headquarters
    Then Headquarters exits with code 0
