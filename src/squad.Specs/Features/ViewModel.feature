Feature: Squad ViewModel
  The application ViewModel isolates role state and serializes role commands.

  Scenario: UI snapshot contains role state and pending interactions
    Given a ViewModel with recording roles "coder"
    When the recording "coder" session emits a started event
    And the recording "coder" session emits assistant delta "Working on the change."
    And the recording "coder" session emits tool start "read_file"
    And the recording "coder" session requests permission "permission-1"
    And the recording "coder" session requests input "input-1"
    And the recording "coder" session requests URL elicitation "elicitation-1"
    Then the UI snapshot contains the running "coder" role with active tool "read_file"
    And the UI snapshot contains an "assistant" transcript entry "Working on the change." for "coder"
    And the UI snapshot contains pending permission "permission-1" for "coder"
    And the UI snapshot contains pending input "input-1" for "coder"
    And the UI snapshot contains pending elicitation "elicitation-1" for "coder"

  Scenario: UI snapshots remain available while a role streams updates
    Given a ViewModel with recording roles "coder"
    When the ViewModel creates snapshots while recording "coder" emits 100 assistant updates
    Then the UI snapshot contains event count 100 for "coder"

  Scenario: SDK-shaped agent backend publishes early events and initial instructions
    Given a SquadApplication with SDK-shaped recording roles "coder,reviewer"
    When the application lifecycle reaches readiness
    Then SDK-shaped sessions use distinct role worktrees
    And early SDK-shaped events reached each ViewModel role
    And SDK-shaped initial instructions were sent after session registration
    When the application window closes
    And the application waits for window closure
    Then SDK-shaped sessions were disposed in reverse registration order

  Scenario: External shutdown stops a lease-owned application
    Given a SquadApplication with recording roles and a host lease
    When the leased SquadApplication starts
    And an external client requests application shutdown
    Then the lease-owned application resources are released

  Scenario: Server failure during blocked startup remains primary
    Given a controllable SquadApplication with blocked startup and a faulting server
    When the application lifecycle begins
    And the controllable host fails its server
    Then the application lifecycle failed with "recording host server failed"
    And all controllable application resources were disposed

  Scenario: Shutdown after readiness stops the host without waiting for close
    Given a controllable SquadApplication
    When the application lifecycle reaches readiness
    And the controllable host requests shutdown
    Then the application stopped after readiness
    And readiness was announced once
    And all controllable application resources were disposed

  Scenario: Primary and cleanup failures are both reported
    Given a controllable SquadApplication with startup and cleanup failures
    When the application lifecycle runs
    Then the application lifecycle contains "recording window start failed" and "recording handoff pump disposal failed"
    And all controllable application resources were disposed

  Scenario: Runtime and cleanup failures are both reported
    Given a controllable SquadApplication with runtime and cleanup failures
    When the application lifecycle reaches readiness
    Then the application lifecycle contains "recording window close failed" and "recording handoff pump disposal failed"
    And all controllable application resources were disposed

  Scenario: A startup failure after terminal shutdown is reported
    Given a controllable SquadApplication with a cancellation-failing startup and a faulting server
    When the application lifecycle begins
    And the controllable host fails its server
    Then the application lifecycle contains "recording host server failed" and "recording startup cancellation failed"
    And all controllable application resources were disposed

  Scenario: Roles keep independent lifecycle state
    Given a ViewModel with recording roles "coder,reviewer"
    When the recording "coder" session emits a started event
    And the recording "reviewer" session emits an error "backend unavailable"
    Then ViewModel role "coder" has status "running"
    And ViewModel role "reviewer" has status "error"
    And ViewModel role "coder" has no error