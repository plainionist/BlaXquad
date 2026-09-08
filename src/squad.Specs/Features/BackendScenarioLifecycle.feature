Feature: Backend scenario cleanup isolation and bounded diagnostics

  BackendScenario cleanup must be bounded, must never let a cleanup failure replace a scenario's real result, and
  emergency termination must target only the exact process a scenario itself launched - even while another
  headquarters process from a different scenario is still running.

  Scenario: Emergency cleanup for one backend scenario never disturbs another running headquarters process
    Given two independent backend scenarios "first" and "second", each configured with a "coder" role
    And both backend scenarios have started squad-hq with the echo provider fixture
    When the "first" backend scenario is disposed without a normal shutdown
    Then the "first" backend scenario process has exited
    And the "second" backend scenario process is still running

  Scenario: A temporary workspace whose directory cannot be removed still disposes without throwing
    Given a git project workspace with a file locked open inside it
    When the workspace is disposed
    Then disposal completes without throwing within the bounded cleanup window
