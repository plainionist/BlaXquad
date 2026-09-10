Feature: UI protocol validation
  squad-hq validates every UI command against its versioned JSON envelope contract - independently of whether the
  command originates from the visual Photino window or the headless stdio transport these specifications drive
  through the real, separately launched process. An invalid envelope produces the documented "protocol.error"
  message, is rejected before it ever reaches the role's provider session, and never disturbs the process's
  ability to keep serving later, valid commands.

  Background:
    Given a backend scenario configured with a "coder" role
    And the backend scenario has enabled the fake-provider control transport
    When the backend scenario starts squad-hq with the fake provider fixture
    Then the backend scenario observes a session started for role "coder" across the control pipe

  Scenario Outline: An invalid UI message is rejected before reaching the role's session
    When the backend scenario sends the invalid "<case>" ui message
    Then the backend scenario observes its exact protocol error for the rejected message
    And no provider-side command was invoked for the rejected message
    And the backend scenario remains usable after the rejected message

    Examples:
      | case                             |
      | unsupported version              |
      | missing type                     |
      | unknown type                     |
      | missing role                     |
      | missing request ID               |
      | invalid string payload           |
      | invalid boolean payload          |
      | invalid integer payload          |
      | invalid synchronization payload  |
      | malformed JSON                   |

  Scenario: A prompt sent to an unknown role is reported as a protocol error without disturbing the process
    When the backend scenario sends a prompt to the unknown role "not-a-configured-role"
    Then the backend scenario observes its exact protocol error for the rejected message
    And no provider-side command was invoked for the rejected message
    And the backend scenario remains usable after the rejected message
