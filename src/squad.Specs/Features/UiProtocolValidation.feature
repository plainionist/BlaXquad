Feature: UI protocol validation
  squad-hq validates every UI command against its versioned JSON envelope contract - independently of whether the
  command originates from the visual Photino window or the headless stdio transport these specifications drive
  through the real, separately launched process. An invalid envelope produces the documented "protocol.error"
  message, is rejected before it ever reaches the role's provider session, and never disturbs the process's
  ability to keep serving later, valid commands.

  Background:
    Given `blaxquad/squad.json` configures:
      | role  |
      | coder |
    When the operator launches Headquarters
    Then Headquarters starts an agent session for role "coder"

  Scenario Outline: An invalid UI message is rejected before reaching the role's session
    When a UI-protocol client sends the invalid "<case>" envelope:
      """
      <envelope>
      """
    Then a UI-protocol client observes the protocol error "<expected error>"
    And no provider-side command was invoked for the rejected message
    And a UI-protocol client's later command still succeeds

    Examples:
      | case                             | envelope                                                                                                | expected error |
      | unsupported version              | {"version":2,"type":"role.abort","role":"coder"}                                                        | The UI protocol version is not supported. |
      | missing type                      | {"version":3}                                                                                            | The UI message is missing a type. |
      | unknown type                      | {"version":3,"type":"unknown"}                                                                           | Unknown UI message type 'unknown'. |
      | missing role                      | {"version":3,"type":"prompt.send","payload":{"prompt":"hello"}}                                         | The UI message is missing role. |
      | missing request ID                | {"version":3,"type":"permission.respond","role":"coder","payload":{"approved":true}}                    | The UI message is missing requestId. |
      | invalid string payload            | {"version":3,"type":"prompt.send","role":"coder","payload":{"prompt":42}}                               | The UI message is missing payload.prompt. |
      | invalid boolean payload           | {"version":3,"type":"permission.respond","role":"coder","requestId":"permission-1","payload":{"approved":"yes"}} | The UI message is missing payload.approved. |
      | invalid integer payload           | {"version":3,"type":"transcript.page","role":"coder","payload":{"beforeIndex":"five"}}                  | The requested operation requires an element of type 'Number', but the target element has type 'String'. |
      | invalid synchronization payload   | {"version":3,"type":"transcript.synchronize","payload":{"roles":"coder"}}                                | The UI message contains invalid transcript positions. |
      | malformed JSON                    | {                                                                                                         | Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed. LineNumber: 0 \| BytePositionInLine: 1. |

  Scenario: A prompt sent to an unknown role is reported as a protocol error without disturbing the process
    When a UI-protocol client sends a prompt to the unknown role "not-a-configured-role"
    Then a UI-protocol client observes the protocol error "Unknown role: not-a-configured-role"
    And no provider-side command was invoked for the rejected message
    And a UI-protocol client's later command still succeeds
