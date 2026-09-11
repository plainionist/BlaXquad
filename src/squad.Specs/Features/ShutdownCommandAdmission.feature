Feature: Draining accepted commands and rejecting new commands during shutdown

  A real host-control shutdown lets an already-admitted prompt reach its own safe, canceled outcome before the
  owning session is disposed, while any further protocol command arriving after admission closes is observably
  rejected - with no prompt, transcript entry, or other durable side effect - and backend cleanup, once genuinely
  held at the real provider boundary, still completes cleanly afterward with a bounded zero exit and fully
  released host, provider, and endpoint ownership - proven only through the real process, the real "squad-hq
  shutdown" host-control command, and the fake-provider control pipe, never through SquadViewModel or
  SquadApplication directly.

  Background:
    Given `blaxquad/squad.json` configures:
      | role  |
      | coder |
    When the operator launches Headquarters
    Then Headquarters starts an agent session for role "coder"

  Scenario: Shutdown drains an admitted prompt and rejects a new one while cleanup holds at the provider boundary
    When the "coder" agent holds its next session disposal pending
    And the user sends "keep this admitted" to role "coder"
    Then the "coder" agent observes the prompt "keep this admitted"
    When the operator begins shutting down Headquarters without waiting for it to exit
    Then the "coder" agent's session disposal is held after its admitted send is already canceled
    And the user observes a protocol error mentioning "task was canceled"
    When the user sends "rejected after admission closes" to role "coder"
    Then the user observes a protocol error mentioning "shutting down"
    And the "coder" agent has not received the prompt "rejected after admission closes" within 2 seconds
    And the transcript for role "coder" does not contain "rejected after admission closes" within 2 seconds
    When the "coder" agent releases its pending session disposal
    And Headquarters' process exits on its own
    Then Headquarters exits with code 0
    And Headquarters disposes the agent session for role "coder"
    And the operator finds Headquarters unavailable for role "coder"
