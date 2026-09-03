Feature: Photino UI protocol
  Photino routes serialized UI commands and reports protocol failures without a native window.

  Background:
    Given a recording Photino protocol host

  Scenario: Readiness initializes transcript synchronization
    When the UI sends its readiness command
    Then UI readiness is signaled
    And the initial transcript high-water mark is requested
    And an initial transcript synchronization is serialized

  Scenario: Transcript synchronization uses the transcript UI
    When the UI requests transcript synchronization
    Then a recovery transcript synchronization is serialized

  Scenario: Transcript pages use the transcript UI
    When the UI requests a transcript page
    Then the requested transcript page is serialized

  Scenario: Archived transcript entries use the transcript UI
    When the UI requests an archived transcript entry
    Then the requested archived transcript entry is serialized

  Scenario: Agent commands route with their original values
    When the UI sends a prompt command
    And the UI sends an abort command
    And the UI sends a permission response
    And the UI sends an input response
    And the UI sends a form elicitation response
    Then the recording UI received these calls in order:
      | call                                                        |
      | prompt:coder:hello                                           |
      | abort:coder                                                  |
      | permission:coder:permission-1:true                           |
      | input:reviewer:input-1:typed:false                           |
      | elicitation.lookup:writer:form-1                             |
      | elicitation.complete:writer:form-1:accept:{ "answer": "okay" } |
    And no protocol message is serialized

  Scenario: Accepted URL elicitations complete before opening
    When the UI begins accepting a URL elicitation
    Then the URL is not opened before response completion
    When URL elicitation completion finishes
    Then the recording UI received these calls in order:
      | call                                                     |
      | elicitation.lookup:writer:url-1                          |
      | elicitation.complete.started:writer:url-1:accept:null    |
      | elicitation.complete:writer:url-1:accept:null            |
      | url.open:https://example.test/authorize                  |
    And no protocol message is serialized

  Scenario Outline: Invalid messages publish their exact protocol error
    When the invalid "<case>" UI message is received
    Then its exact protocol error is serialized
    And no UI or transcript command was invoked

    Examples:
      | case                            |
      | unsupported version             |
      | missing type                    |
      | unknown type                    |
      | missing role                    |
      | missing request ID              |
      | invalid string payload          |
      | invalid boolean payload         |
      | invalid integer payload         |
      | invalid synchronization payload |
      | malformed JSON                  |

  Scenario Outline: Envelope error publication failures reach the outer boundary
    Given the next protocol send fails with "recording sink failed"
    When the invalid "<case>" UI message is received
    Then protocol error "recording sink failed" is serialized
    And protocol errors "<validation error>" and "recording sink failed" were attempted

    Examples:
      | case                | validation error                           |
      | unsupported version | The UI protocol version is not supported. |
      | missing type        | The UI message is missing a type.          |

  Scenario: Backend command exceptions become protocol errors
    Given prompt commands fail with "recording send failed"
    When the UI sends a prompt command
    Then protocol error "recording send failed" is serialized
    And the recording UI received these calls in order:
      | call               |
      | prompt:coder:hello |

  Scenario: Unknown elicitations fail lookup before completion
    When the UI responds to an unknown elicitation
    Then protocol error "Unknown elicitation 'missing' for role 'writer'." is serialized
    And the recording UI received these calls in order:
      | call                                       |
      | elicitation.lookup:writer:missing          |
