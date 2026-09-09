Feature: Draining accepted commands and rejecting new commands during shutdown

  A real host-control shutdown lets an already-admitted prompt reach its own safe, canceled outcome before the
  owning session is disposed, while any further protocol command arriving after admission closes is observably
  rejected - with no prompt, transcript entry, or other durable side effect - and backend cleanup, once genuinely
  held at the real provider boundary, still completes cleanly afterward with a bounded zero exit and fully
  released host, provider, and endpoint ownership - proven only through the real process, the real "squad-hq
  shutdown" host-control command, and the fake-provider control pipe, never through SquadViewModel or
  SquadApplication directly.

  Scenario: Shutdown drains an admitted prompt and rejects a new one while cleanup holds at the provider boundary
    Given a backend scenario configured with roles "coder"
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe
    When the backend scenario arms role "coder" to hold its next session disposal pending
    And the backend scenario sends the prompt "keep this admitted" to role "coder"
    Then the "coder" agent observes the prompt "keep this admitted"
    When the backend scenario requests a host-control shutdown without waiting for the process to exit
    Then the backend scenario observes role "coder"'s prompt "keep this admitted" canceled before disposal
    And the backend scenario observes role "coder"'s session disposal held
    And the backend scenario observes a protocol error mentioning "task was canceled"
    When the backend scenario sends the prompt "rejected after admission closes" to role "coder"
    Then the backend scenario observes a protocol error mentioning "shutting down"
    And the "coder" agent has not received the prompt "rejected after admission closes" within 2 seconds
    And the backend scenario does not observe the transcript for role "coder" containing "rejected after admission closes" within 2 seconds
    When the backend scenario completes the pending session disposal for role "coder"
    And the backend scenario waits for the process to exit on its own
    Then the backend scenario observes an exit code of zero
    And the backend scenario observes a session disposed for role "coder" across the control pipe
    And the backend scenario confirms host control is unavailable for role "coder"
