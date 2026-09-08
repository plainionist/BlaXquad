Feature: The vertical architectural proof of the process-level backend specification driver

  One black-box scenario that crosses every process and protocol boundary the process-level backend specification
  driver establishes: a configured fake role, the real provider-free headquarters, the real "ui.ready" handshake,
  the role's session observed both through the real UI status projection and through the private control pipe, a
  prompt sent through the real UI protocol and observed through the semantic role controller, a semantic reply that
  crosses the pipe and is translated inside the launched process to the real production assistant event, that reply
  observed in the real transcript projection, and a clean shutdown through the real host-control command. Every step
  uses only the semantic workspace, UI, agent, CLI, and lifecycle operations of the scenario facade - never a
  file-system path, a process handle, a control-pipe DTO, or any other product object graph - and every wait is
  bounded with combined process, UI, and provider diagnostics; there are no arbitrary sleeps anywhere in this proof.

  Scenario: The full request-response path crosses the workspace, CLI, UI, control pipe, and provider boundaries and shuts down cleanly
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario reports the process as ready
    And the backend scenario observes role "coder" at status "running"
    And the backend scenario observes a session started for role "coder" across the control pipe
    When the backend scenario sends the prompt "What should I build?" to role "coder"
    Then the "coder" agent observes the prompt "What should I build?"
    When the "coder" agent replies with "Build the driver."
    Then the backend scenario observes the transcript for role "coder" containing "Build the driver."
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero

  Scenario: A bounded wait used by the vertical proof reports combined process, UI protocol, and provider diagnostics on timeout
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    When the backend scenario waits 1 seconds for role "coder" at status "bogus-status-that-never-happens"
    Then the wait fails with a diagnostics block naming the process, the UI protocol state, and the provider observations
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero
