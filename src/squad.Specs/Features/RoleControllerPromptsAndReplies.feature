Feature: Role controller exposes prompts and assistant replies

  A role controller returned by "scenario.Agent(role)" is the only surface Gherkin steps use to observe a prompt
  a role received and send it a semantic assistant reply - the control transport's DTOs and the production
  provider's AgentEvent values stay entirely behind it. A prompt sent through the real UI protocol is observed
  through this controller; a reply sent through it crosses the private control pipe, is translated inside the
  launched process to the real production assistant event, and appears in the real transcript projection - proving
  the whole path from UI input to provider-neutral output without any product test hook.

  Scenario: A prompt sent through the real UI protocol is observed and answered through the role controller
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    When the backend scenario sends the prompt "What should I build?" to role "coder"
    Then the "coder" agent observes the prompt "What should I build?"
    When the "coder" agent replies with "Build the driver."
    Then the backend scenario observes the transcript for role "coder" containing "Build the driver."
    When the backend scenario requests a host-control shutdown
    Then the backend scenario observes an exit code of zero
