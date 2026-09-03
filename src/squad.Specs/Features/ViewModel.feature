Feature: Squad ViewModel
  The application ViewModel isolates role state and serializes role commands.

  Scenario: SquadApplication forwards and drains session events
    Given a SquadApplication with recording roles "coder,reviewer"
    When the SquadApplication starts
    And the application recording "coder" session emits a started event
    Then the application ViewModel role "coder" has status "running"
    And the application ViewModel role "coder" saw one event
    When the SquadApplication stops
    Then the recording application sessions are drained

  Scenario: Failed role state is terminal and clears its pending interactions
    Given a SquadApplication with recording roles "coder,reviewer"
    When the SquadApplication starts
    And the application recording "coder" session requests permission "permission-1"
    And the application recording "coder" session fails with "event channel overloaded"
    And a late started event is submitted for application role "coder"
    And prompt "still available" is sent to application role "reviewer"
    Then the application ViewModel role "coder" has error "event channel overloaded"
    And the application ViewModel has no pending interactions for "coder"
    And the application recording "reviewer" session received prompt "still available"
    When the SquadApplication stops
    Then the recording application sessions are drained

  Scenario: Backend-wide terminal failure stops the application
    Given a SquadApplication with recording roles "coder,reviewer"
    When the SquadApplication starts
    And the recording backend reports terminal failure "shared SDK force-stop failed"
    Then the application lifecycle fails after cleanup
    And the application lifecycle contains "shared SDK force-stop failed"

  Scenario: Session failure during normal shutdown is not a cleanup failure
    Given a SquadApplication with recording roles "coder"
    When the SquadApplication starts
    And the application window closes while recording "coder" fails
    And the application waits for window closure
    Then the recording application sessions are drained

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

  Scenario: UI snapshots preserve transcript entry field names
    Given a ViewModel with recording roles "coder"
    When the recording "coder" session emits assistant delta "Ready"
    Then the UI snapshot contains an "assistant" transcript entry "Ready" for "coder"
    And the UI state snapshot excludes transcript history

  Scenario: Transcript updates preserve ordering and stream semantics
    Given a ViewModel with recording roles "coder"
    When the recording "coder" session emits a user message "question"
    And the recording "coder" session emits assistant delta "Hello-"
    And the recording "coder" session emits assistant delta "world"
    And the recording "coder" session emits a final assistant message "Hello-world"
    Then the transcript updates for "coder" are
      | sequence | operation     | index | content     |
      | 1        | AppendEntry   | 0     | question    |
      | 2        | AppendEntry   | 1     | Hello-      |
      | 3        | AppendContent | 1     | world       |
      | 4        | ReplaceEntry  | 1     | Hello-world |
    And the transcript announcements for "coder" are
      | sequence | operation     | content     |
      | 1        | AppendEntry   | question    |
      | 2        | AppendEntry   | Hello-      |
      | 3        | AppendContent | world       |
    And a 2 update recovery announcement journal for "coder" reports truncation after sequence 0
    And the Photino transcript delta excludes earlier transcript content
    And the Photino recovery synchronization includes announcements for "coder" after sequence 1
    And the Photino transcript synchronization preserves the current "coder" history

  Scenario: Transcript synchronization pages older history
    Given a ViewModel with recording roles "coder"
    When the recording "coder" session emits 505 user messages
    Then a 500 entry transcript synchronization for "coder" starts at index 5
    And the previous transcript page for "coder" contains the first 5 entries

  Scenario: Transcript state and announcement publication are atomic
    Given a ViewModel with recording roles "coder"
    When transcript publication for "coder" pauses after an assistant delta "atomic"
    Then transcript snapshot capture waits for the publication and includes announcement "atomic"

  Scenario: Initial transcript synchronization replays updates after its high-water mark
    Given a ViewModel with recording roles "coder"
    When the recording "coder" session emits a user message "historical"
    And the initial transcript high-water mark for "coder" is captured
    And the recording "coder" session emits assistant delta "live"
    Then initial transcript synchronization for "coder" announces "live" exactly once after the high-water mark

  Scenario: Transcript retention spills older entries out of live state
    Given a ViewModel retaining 3 entries and 30 content characters
    When the recording "coder" session emits 6 user messages
    Then ViewModel role "coder" retains at most 3 entries and 30 content characters
    And the retained transcript for "coder" starts at index 3
    And the previous transcript page for "coder" contains the first 3 entries
    When the bounded ViewModel is disposed
    Then its temporary transcript history is removed

  Scenario: Oversized transcript entries cannot bypass the memory bound
    Given a ViewModel retaining 3 entries and 30 content characters
    When the recording "coder" session emits a user message "abcdefghijklmnopqrstuvwxyz0123456789"
    Then ViewModel role "coder" retains at most 3 entries and 30 content characters
    And the retained transcript entry for "coder" offers archived content
    And archived transcript history for "coder" preserves "abcdefghijklmnopqrstuvwxyz0123456789"

  Scenario: Oversized transcript announcements remain bounded
    Given a ViewModel with recording roles "coder"
    When the recording "coder" session emits a 20000 character user message
    Then the latest transcript announcement contains 16384 characters and reports truncation

  Scenario: Pending interaction context remains retained
    Given a ViewModel retaining 2 entries and 50 content characters
    When the recording "coder" session requests permission "permission-1"
    And the recording "coder" session emits 3 user messages
    Then ViewModel role "coder" transcript has a "harness" entry "Permission required: Run command."
    And ViewModel role "coder" retains at most 2 entries and 50 content characters
    And transcript synchronization for "coder" reports older history

  Scenario: Active streaming context remains retained
    Given a ViewModel retaining 2 entries and 30 content characters
    When the recording "coder" session emits assistant delta "active"
    And the recording "coder" session emits 3 harness messages
    Then ViewModel role "coder" transcript has a "assistant" entry "active"
    And ViewModel role "coder" retains at most 2 entries and 30 content characters

  Scenario: Idle finalizes a reasoning stream
    Given a ViewModel retaining 2 entries and 30 content characters
    When the recording "coder" session emits reasoning delta "first"
    And the recording "coder" session emits an idle event
    And the recording "coder" session emits reasoning delta "second"
    Then ViewModel role "coder" transcript has a "reasoning" entry "first"
    And ViewModel role "coder" transcript has a "reasoning" entry "second"

  Scenario: Archived transcript history has an explicit disk bound
    Given a ViewModel retaining 2 entries and archiving 3 entries
    When the recording "coder" session emits 6 user messages
    Then archived transcript history for "coder" contains 3 entries and reports truncation

  Scenario: Archived streaming content reports exact-limit truncation
    Given a ViewModel archiving 80 characters per entry
    When the recording "coder" session emits assistant delta "12345678901234567890123456789012345678901234567890123456789012345678901234567890"
    And the recording "coder" session emits assistant delta "overflow"
    And the recording "coder" session emits assistant delta "ignored after truncation"
    And the recording "coder" session emits an idle event
    Then archived transcript history for "coder" contains a truncation marker

  Scenario: Synchronization invalidates rotated archived content
    Given a ViewModel retaining 2 entries and 60 content characters while archiving 3 entries
    When the recording "coder" session emits assistant delta "1234567890123456789012345678901234567890123456789012345678901234567890"
    And the recording "coder" session emits 5 harness messages
    Then synchronized transcript history for "coder" reports unavailable archived content within 30 characters

  Scenario: UI snapshots include real context usage
    Given a ViewModel with recording roles "coder"
    When the recording "coder" session reports 48000 context tokens of 128000
    Then the UI snapshot contains 48000 context tokens of 128000 for "coder"

  Scenario: UI snapshots include accumulated AIC usage
    Given a ViewModel with recording roles "coder"
    When the recording "coder" session reports 3.5 AIC used
    Then the UI snapshot contains 3.5 AIC used for "coder"

  Scenario: A delayed AIC refresh cannot overwrite a newer usage checkpoint
    Given a ViewModel with recording roles "coder"
    When the recording "coder" session reports 3.5 AIC used
    And the recording "coder" session reports 0 AIC used
    Then the UI snapshot contains 3.5 AIC used for "coder"

  Scenario: SDK-shaped agent backend publishes early events and initial instructions
    Given a SquadApplication with SDK-shaped recording roles "coder,reviewer"
    When the application lifecycle reaches readiness
    Then SDK-shaped sessions use distinct role worktrees
    And early SDK-shaped events reached each ViewModel role
    And SDK-shaped initial instructions were sent after session registration
    When the application window closes
    And the application waits for window closure
    Then SDK-shaped sessions were disposed in reverse registration order

  Scenario: SDK-shaped agent backend unwinds partial startup
    Given a SquadApplication with SDK-shaped recording roles "coder,reviewer"
    And the SDK-shaped backend fails after its first session
    When the application start fails
    Then the application start failed
    And the application cleaned up its startup resources
    And all SDK-shaped sessions were disposed

  Scenario: SDK-shaped session errors reach the ViewModel before shutdown
    Given a SquadApplication with SDK-shaped recording roles "coder"
    When the application lifecycle reaches readiness
    And the recording "coder" session emits an error "SDK unavailable"
    Then the application ViewModel role "coder" has error "SDK unavailable"
    When the application window closes
    And the application waits for window closure
    Then SDK-shaped sessions were disposed in reverse registration order

  Scenario: Healthy startup with host ownership completes
    Given a SquadApplication with recording roles and a host lease
    When the leased SquadApplication starts
    Then the leased SquadApplication start completes
    When the leased SquadApplication stops

  Scenario: External shutdown stops a lease-owned application
    Given a SquadApplication with recording roles and a host lease
    When the leased SquadApplication starts
    And an external client requests application shutdown
    Then the lease-owned application resources are released

  Scenario: External shutdown pending before lifecycle prevents startup
    Given a SquadApplication with recording roles and a host lease
    When an external client begins requesting application shutdown
    And the lease-owned application lifecycle runs
    Then the application stopped before readiness
    And no startup collaborator ran
    And the lease-owned application resources are released

  Scenario: External shutdown cancels blocked lease-owned preparation
    Given a lease-owned SquadApplication with blocked preparation
    When the lease-owned application lifecycle begins preparation
    And an external client requests application shutdown
    Then the application stopped before readiness
    And the blocked preparation observed cancellation
    And the lease-owned application resources are released

  Scenario: SquadApplication cleans up after window closure
    Given a SquadApplication with recording roles "coder"
    When the SquadApplication starts
    And the application window closes
    And the application waits for window closure
    Then the recording application sessions are drained

  Scenario: Late session events do not fail window shutdown
    Given a SquadApplication with a session that emits while shutting down
    When the application lifecycle reaches readiness
    And the application window closes
    And the application waits for window closure
    Then the recording application sessions are drained

  Scenario: In-process polling waits for registration and serializes a busy recipient
    Given a SquadApplication with an in-process handoff poller and a pending handoff
    When the application lifecycle reaches readiness
    Then the pending handoff wakes the registered recipient after terminal sessions start
    And the in-process recipient session had no overlapping sends
    When the application window closes
    And the application waits for window closure
    Then the recording application sessions are drained

  Scenario Outline: In-process polling preserves delivery when a recipient is unavailable
    Given a SquadApplication with an in-process handoff poller and a <state> recipient
    And the in-process poller has a pending handoff
    When the application lifecycle reaches readiness
    Then the in-process handoff is archived and the notification failure is logged
    When the application window closes
    And the application waits for window closure
    Then the recording application sessions are drained

    Examples:
      | state   |
      | missing |
      | stopped |
      | failed  |

  Scenario: In-process polling recovers inbox work without mutating it
    Given a SquadApplication with an in-process handoff poller and recovered inbox work
    When the application lifecycle reaches readiness
    Then the recovered inbox work is unchanged and wakes its recipient once
    When the application window closes
    And the application waits for window closure
    Then the recording application sessions are drained

  Scenario: Caller cancellation stops in-process polling
    Given a cancellable SquadApplication with an in-process handoff poller
    When the application lifecycle reaches readiness
    And in-process polling is canceled
    Then the application lifecycle was canceled
    And the recording application sessions are drained

  Scenario: Startup failure before the window is cleaned up
    Given a SquadApplication that fails before window startup
    When the application start fails
    Then the application start failed
    And the application cleaned up its startup resources
    And the window host start was attempted

  Scenario: CLI startup failure retains its exit type
    Given a SquadApplication with a CLI startup failure
    When the application start fails
    Then the application start failed with a CLI exit exception
    And the application cleaned up its startup resources

  Scenario: Startup failure after the window is cleaned up
    Given a SquadApplication that fails after window startup
    When the application start fails
    Then the application start failed
    And the application cleaned up its startup resources
    And the window host was stopped
    And the recording backend was disposed

  Scenario: Partial backend startup failure is cleaned up
    Given a SquadApplication whose backend fails during startup
    When the application start fails
    Then the application start failed
    And the application cleaned up its startup resources
    And the partial startup observer observed cancellation
    And the window host was stopped
    And the recording backend was disposed

  Scenario: Shutdown already requested prevents startup work
    Given a controllable SquadApplication with shutdown already requested
    When the application lifecycle runs
    Then the application stopped before readiness
    And no startup collaborator ran
    And all controllable application resources were disposed

  Scenario: Shutdown during blocked startup releases all resources
    Given a controllable SquadApplication with blocked startup
    When the application lifecycle begins
    And the controllable host requests shutdown
    Then the application stopped before readiness
    And all controllable application resources were disposed

  Scenario: Server failure during blocked startup remains primary
    Given a controllable SquadApplication with blocked startup and a faulting server
    When the application lifecycle begins
    And the controllable host fails its server
    Then the application lifecycle failed with "recording host server failed"
    And all controllable application resources were disposed

  Scenario: Shutdown wins a simultaneous ready transition
    Given a controllable SquadApplication that requests shutdown when ready
    When the application lifecycle runs
    Then the application stopped before readiness
    And readiness was not announced
    And all controllable application resources were disposed

  Scenario: Shutdown after readiness stops the host without waiting for close
    Given a controllable SquadApplication
    When the application lifecycle reaches readiness
    And the controllable host requests shutdown
    Then the application stopped after readiness
    And readiness was announced once
    And all controllable application resources were disposed

  Scenario: A post-ready handoff failure stops the host
    Given a controllable SquadApplication with a post-ready handoff failure
    When the application lifecycle reaches readiness
    And the controllable handoff pump fails
    Then the application lifecycle failed with "recording handoff pump failed"
    And all controllable application resources were disposed

  Scenario: Caller cancellation wins a simultaneous ready transition
    Given a controllable SquadApplication that cancels its caller when ready
    When the application lifecycle runs
    Then the application lifecycle was canceled
    And readiness was not announced
    And all controllable application resources were disposed

  Scenario: A window close failure is reported after cleanup
    Given a controllable SquadApplication with a failing window close
    When the application lifecycle reaches readiness
    Then the application lifecycle failed with "recording window close failed"
    And all controllable application resources were disposed

  Scenario: Open session events cannot block failed disposal cleanup
    Given a controllable SquadApplication with a session disposal failure and open events
    When the application lifecycle reaches readiness
    And the application window closes
    Then the application lifecycle fails after cleanup
    And the open event observer was canceled without stream completion
    And all controllable application resources were disposed

  Scenario: Backend cleanup retains host ownership until it terminates
    Given a controllable SquadApplication with blocking backend cleanup
    When the application lifecycle reaches readiness
    And the application window closes
    And backend cleanup begins
    And backend cleanup remains blocked for six seconds
    Then the host lease remains held
    When backend cleanup is released
    And the application waits for window closure
    Then the application stopped after readiness
    And all controllable application resources were disposed

  Scenario: Shutdown rejects commands before session disposal
    Given a controllable SquadApplication that sends a command while stopping
    When the application lifecycle reaches readiness
    And the application window closes
    Then the stopping command was rejected
    And all controllable application resources were disposed

  Scenario: Accepted commands drain before session disposal
    Given a controllable SquadApplication with an in-flight command
    When the application lifecycle reaches readiness
    And the in-flight application command begins
    And the application window closes
    Then the accepted command was canceled before its session disposal
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

  Scenario: Streaming assistant messages aggregate in one transcript entry
    Given a ViewModel with recording roles "coder"
    When the recording "coder" session emits assistant delta "Hello "
    And the recording "coder" session emits assistant delta "world"
    Then ViewModel role "coder" transcript has a "assistant" entry "Hello world"

  Scenario: Snapshot materialization keeps assistant streaming active
    Given a ViewModel with recording roles "coder"
    When the recording "coder" session emits assistant delta "Hello "
    Then the UI snapshot contains an "assistant" transcript entry "Hello " for "coder"
    When the recording "coder" session emits assistant delta "world"
    Then ViewModel role "coder" transcript has a "assistant" entry "Hello world"

  Scenario: Final reasoning replaces streamed reasoning
    Given a ViewModel with recording roles "coder"
    When the recording "coder" session emits reasoning delta "draft"
    And the recording "coder" session emits final reasoning "final"
    Then ViewModel role "coder" transcript has a "reasoning" entry "final"
    And ViewModel role "coder" transcript has no entry "draft"

  Scenario: Transcript entries preserve their sources
    Given a ViewModel with recording roles "coder"
    When the recording "coder" session emits a user message "question"
    And the recording "coder" session emits harness message "Loading instructions"
    And the recording "coder" session emits assistant delta "answer"
    And the recording "coder" session emits tool start "read_file"
    Then ViewModel role "coder" transcript has a "user" entry "question"
    And ViewModel role "coder" transcript has a "harness" entry "Loading instructions"
    And ViewModel role "coder" transcript has a "assistant" entry "answer"
    And ViewModel role "coder" transcript has a "read" entry "read_file"

  Scenario Outline: Subagent activity uses semantic transcript metadata
    Given a ViewModel with recording roles "coder"
    When the recording "coder" session starts subagent "<agent>" displayed as "<description>" using model "<model>"
    Then ViewModel role "coder" transcript has a "subagent" entry "<transcript>"

    Examples:
      | agent      | description                   | model         | transcript                                                |
      | code-review | Review authentication changes | gpt-5.6-sol   | Code Review · gpt-5.6-sol · Review authentication changes |
      | explore     | Explore                       | claude-sonnet | Explore · claude-sonnet                                  |
      | explore     | Find authentication entries   |               | Explore · Find authentication entries                    |
      |             |                               |               | Subagent                                                 |

  Scenario: Subagent plumbing is omitted from the transcript
    Given a ViewModel with recording roles "coder"
    When the recording "coder" session emits tool start "task"
    And the recording "coder" session emits tool start "read_agent"
    And the recording "coder" session emits tool start "list_agents"
    Then ViewModel role "coder" transcript has exactly 0 "tool" entries

  Scenario: Console activity preserves streaming tool output without completion summaries
    Given a ViewModel with recording roles "coder"
    When the recording "coder" session emits reasoning delta "Checking the workspace"
    And the recording "coder" session emits tool start "run_in_terminal"
    And the recording "coder" session emits tool output "3 files found"
    And the recording "coder" session emits tool completion "run_in_terminal" with output "Build succeeded"
    Then ViewModel role "coder" is working
    And ViewModel role "coder" transcript has a "reasoning" entry "Checking the workspace"
    And ViewModel role "coder" transcript has a "tool" entry:
      """
      run_in_terminal
      3 files found
      """
    And ViewModel role "coder" transcript has no entry "Build succeeded"

  Scenario: Cumulative tool snapshots update one transcript item
    Given a ViewModel with recording roles "coder"
    When SDK tool call "X" starts "powershell" for role "coder"
    And SDK tool call "X" emits partial output "LINE-1" for role "coder"
    And SDK tool call "X" emits partial output "LINE-1\nLINE-2" for role "coder"
    And SDK tool call "X" emits partial output "LINE-1\nLINE-2" for role "coder"
    And SDK tool call "X" completes for role "coder" with detailed output "LINE-1\nLINE-2"
    Then ViewModel role "coder" transcript has exactly 1 "tool" entry
    And ViewModel role "coder" transcript has a "tool" entry:
      """
      powershell
      LINE-1
      LINE-2
      """

  Scenario: Snapshot mode replaces rewritten output
    Given a ViewModel with recording roles "coder"
    When SDK tool call "X" starts "powershell" for role "coder"
    And SDK tool call "X" emits partial output "Progress 10" for role "coder"
    And SDK tool call "X" emits partial output "Progress 10\n" for role "coder"
    And SDK tool call "X" emits partial output "Progress 20" for role "coder"
    Then ViewModel role "coder" transcript has exactly 1 "tool" entry
    And ViewModel role "coder" transcript has a "tool" entry:
      """
      powershell
      Progress 20
      """

  Scenario: Incremental tool chunks update one transcript item
    Given a ViewModel with recording roles "coder"
    When SDK tool call "X" starts "powershell" for role "coder"
    And SDK tool call "X" emits partial output "LINE-1\n" for role "coder"
    And SDK tool call "X" emits partial output "LINE-2\n" for role "coder"
    And SDK tool call "X" emits partial output "LINE-3\n" for role "coder"
    Then ViewModel role "coder" transcript has exactly 1 "tool" entry
    And ViewModel role "coder" transcript has a "tool" entry:
      """
      powershell
      LINE-1
      LINE-2
      LINE-3
      """

  Scenario: Concurrent tool output remains correlated by tool call ID
    Given a ViewModel with recording roles "coder"
    When SDK tool call "X" starts "powershell X" for role "coder"
    And SDK tool call "Y" starts "powershell Y" for role "coder"
    And SDK tool call "X" emits partial output "X-1\n" for role "coder"
    And SDK tool call "Y" emits partial output "Y-1\n" for role "coder"
    And SDK tool call "X" emits partial output "X-2\n" for role "coder"
    And SDK tool call "Y" emits partial output "Y-2\n" for role "coder"
    Then ViewModel role "coder" transcript has exactly 2 "tool" entries
    And ViewModel role "coder" transcript has a "tool" entry:
      """
      powershell X
      X-1
      X-2
      """
    And ViewModel role "coder" transcript has a "tool" entry:
      """
      powershell Y
      Y-1
      Y-2
      """

  Scenario: Tool progress remains separate from output
    Given a ViewModel with recording roles "coder"
    When SDK tool call "X" starts "powershell" for role "coder"
    And tool call "X" reports progress "Waiting" for role "coder"
    And SDK tool call "X" emits partial output "DONE" for role "coder"
    And tool call "X" reports progress "Finishing" for role "coder"
    And SDK tool call "X" completes for role "coder" with detailed output "DONE\nmetadata"
    Then ViewModel role "coder" transcript has exactly 1 "tool" entry
    And ViewModel role "coder" transcript has a "tool" entry:
      """
      powershell
      DONE
      """

  Scenario: Completion detailed output is a display fallback
    Given a ViewModel with recording roles "coder"
    When SDK tool call "X" starts "powershell" for role "coder"
    And SDK tool call "X" completes for role "coder" with detailed output "FINAL"
    Then ViewModel role "coder" transcript has exactly 1 "tool" entry
    And ViewModel role "coder" transcript has a "tool" entry:
      """
      powershell
      FINAL
      """

  Scenario: Console activity formats known command arguments
    Given a ViewModel with recording roles "coder"
    When the recording "coder" session emits tool start "powershell" with arguments:
      """
      {"command":"Get-ChildItem -Path \u0022C:\\work\u0022","description":"List files"}
      """
    Then ViewModel role "coder" transcript has a decoded PowerShell command

  Scenario: Console activity preserves unrecognized tool arguments
    Given a ViewModel with recording roles "coder"
    When the recording "coder" session emits tool start "glob" with arguments:
      """
      {"pattern":"**/*","paths":"C:\\work"}
      """
    Then ViewModel role "coder" transcript has raw glob arguments

  Scenario: File reads include their path in console activity
    Given a ViewModel with recording roles "coder"
    When the recording "coder" session emits tool start "read_file" for "src/App.cs"
    Then ViewModel role "coder" transcript has a "read" entry "src/App.cs"

  Scenario: File-view tools include their path in console activity
    Given a ViewModel with recording roles "coder"
    When the recording "coder" session emits tool start "view_file" for "src/Main.cs"
    Then ViewModel role "coder" transcript has a "read" entry "src/Main.cs"

  Scenario: File reads show their range without echoing file contents
    Given a ViewModel with recording roles "coder"
    When the recording "coder" session emits tool start "view" with arguments:
      """
      {"path":"C:\\work\\src\\Main.cs","view_range":[100,1000]}
      """
    And the recording "coder" session emits tool output "file contents"
    And the recording "coder" session emits tool completion "view" with output "diff --git"
    And the recording "coder" session emits tool start "dotnet build"
    And the recording "coder" session emits tool output "Build succeeded"
    Then ViewModel role "coder" transcript has a "read" entry "C:\work\src\Main.cs [100..1000]"
    And ViewModel role "coder" transcript has no entry "file contents"
    And ViewModel role "coder" transcript has no entry "diff --git"
    And ViewModel role "coder" transcript has a "tool" entry:
      """
      dotnet build
      Build succeeded
      """

  Scenario: Whole-file reads show the number of lines read
    Given a ViewModel with recording roles "coder"
    When the recording "coder" session emits tool start "view" for "C:\work\src\Main.cs"
    And the recording "coder" session emits tool completion "view" with display output "diff --git" and content "first\nsecond\nthird"
    Then ViewModel role "coder" transcript has a "read" entry "C:\work\src\Main.cs [1..3]"
    And ViewModel role "coder" transcript has no entry "first"
    And ViewModel role "coder" transcript has no entry "diff --git"

  Scenario: Tool names containing read verbs remain regular tools
    Given a ViewModel with recording roles "coder"
    When the recording "coder" session emits tool start "preview"
    And the recording "coder" session emits tool output "preview output"
    And the recording "coder" session emits tool start "open_connection"
    And the recording "coder" session emits tool output "connection output"
    And the recording "coder" session emits tool start "thread_status"
    And the recording "coder" session emits tool output "thread output"
    Then ViewModel role "coder" transcript has a "tool" entry:
      """
      preview
      preview output
      """
    And ViewModel role "coder" transcript has a "tool" entry:
      """
      open_connection
      connection output
      """
    And ViewModel role "coder" transcript has a "tool" entry:
      """
      thread_status
      thread output
      """

  Scenario: System activity remains visible in the transcript
    Given a ViewModel with recording roles "coder"
    When the recording "coder" session emits system message "Context compaction started"
    Then ViewModel role "coder" transcript has a "system" entry "Context compaction started"

  Scenario: Skill application is visible in the transcript
    Given a ViewModel with recording roles "coder"
    When the recording "coder" session invokes skill "project-setup"
    Then ViewModel role "coder" transcript has a "tool" entry "using skill(project-setup)"

  Scenario: Skill tool plumbing is omitted from the transcript
    Given a ViewModel with recording roles "coder"
    When the recording "coder" session emits tool start "skill" with arguments:
      """
      {"skill":"project-setup"}
      """
    And the recording "coder" session emits tool output "full SKILL.md contents"
    And the recording "coder" session emits tool completion "skill"
    Then ViewModel role "coder" transcript has no entry "project-setup"
    And ViewModel role "coder" transcript has no entry "full SKILL.md contents"

  Scenario: Skill discovery is distinguished from file reads
    Given a ViewModel with recording roles "coder"
    When the recording "coder" session emits system message "Discovered skill: C:\\skills\\analyze-issue\\SKILL.md"
    Then ViewModel role "coder" transcript has a "system" entry "Discovered skill: C:\\skills\\analyze-issue\\SKILL.md"
    And ViewModel role "coder" transcript has no entry "Read C:\\skills\\analyze-issue\\SKILL.md"

  Scenario: Prompt dispatch remains active until the SDK reports idle
    Given a ViewModel with recording roles "coder"
    When a prompt "question" is sent to "coder"
    Then ViewModel role "coder" is working
    When the recording "coder" session emits an idle event
    Then ViewModel role "coder" has status "idle"
    And ViewModel role "coder" is not working

  Scenario: Agent readiness requires idle state without active work
    Given a ViewModel with recording roles "coder"
    Then ViewModel role "coder" is not ready for a prompt
    When the recording "coder" session emits an idle event
    Then ViewModel role "coder" is ready for a prompt
    When the recording "coder" session emits a user message "new work"
    Then ViewModel role "coder" is not ready for a prompt
    When the recording "coder" session emits an idle event
    And the ViewModel begins stopping
    Then ViewModel role "coder" is not ready for a prompt

  Scenario: Readiness remains safe while sessions register sequentially
    Given a leased application blocked before registering its "reviewer" session
    When the host client begins waiting for the "reviewer" agent
    Then the host client remains waiting for agent readiness
    When the pending session registration completes
    Then the host client readiness wait succeeds
    When the application window closes
    And the application waits for window closure

  Scenario: Final assistant messages preserve prior transcript history
    Given a ViewModel with recording roles "coder"
    When the recording "coder" session emits a user message "question"
    And the recording "coder" session emits a final assistant message "answer"
    Then ViewModel role "coder" transcript has a "user" entry "question"
    And ViewModel role "coder" transcript has a "assistant" entry "answer"

  Scenario: Final assistant messages replace streamed deltas
    Given a ViewModel with recording roles "coder"
    When the recording "coder" session emits a user message "Q"
    And the recording "coder" session emits assistant delta "H"
    And the recording "coder" session emits assistant delta "i"
    And the recording "coder" session emits a final assistant message "Hi"
    Then ViewModel role "coder" transcript has a "user" entry "Q"
    And ViewModel role "coder" transcript has a "assistant" entry "Hi"
    And ViewModel role "coder" transcript has no entry "H"

  Scenario: Interaction requests are visible and can be completed
    Given a ViewModel with recording roles "coder"
    When the recording "coder" session requests permission "permission-1"
    Then ViewModel has one pending permission "permission-1"
    When permission "permission-1" is completed
    Then ViewModel has no pending permission
    When the recording "coder" session requests input "input-1"
    Then ViewModel has one pending input "input-1"
    When the recording "coder" session requests input "input-options" with choices "one,two"
    Then ViewModel input "input-options" has choices "one,two"
    When the recording "coder" session requests elicitation "elicitation-1"
    Then ViewModel has one pending elicitation "elicitation-1"
    When the recording "coder" session requests URL elicitation "elicitation-url"
    Then ViewModel elicitation "elicitation-url" has mode "url"
    And ViewModel elicitation "elicitation-url" has URL "https://example.test/authorize"

  Scenario: Interaction responses are routed to their owning role
    Given a ViewModel with recording roles "coder,reviewer"
    When the recording "coder" session requests permission "permission-1"
    Then ViewModel has one pending permission "permission-1"
    When permission "permission-1" is rejected for "coder"
    Then ViewModel has no pending permission
    And the recording "coder" session received rejected permission "permission-1"
    When the recording "reviewer" session requests input "input-1"
    Then ViewModel has one pending input "input-1"
    When input "input-1" is answered "choice" for "reviewer"
    Then the recording "reviewer" session received input "input-1" with answer "choice"
    When the recording "coder" session requests elicitation "elicitation-1"
    Then ViewModel has one pending elicitation "elicitation-1"
    When elicitation "elicitation-1" is accepted for "coder" with form value "yes"
    Then the recording "coder" session received accepted elicitation "elicitation-1" with form value "yes"

  Scenario: Interaction responses reject wrong-role and late completions
    Given a ViewModel with recording roles "coder,reviewer"
    When the recording "coder" session requests permission "permission-1"
    And permission "permission-1" is approved for "reviewer"
    Then interaction completion is rejected
    And ViewModel has one pending permission "permission-1"
    When permission "permission-1" is approved for "coder"
    Then the recording "coder" session received approved permission "permission-1"
    When permission "permission-1" is approved for "coder"
    Then interaction completion is rejected

  Scenario: Abort and shutdown cancel pending interactions
    Given a ViewModel with recording roles "coder,reviewer"
    When the recording "coder" session requests permission "permission-1"
    And "coder" is aborted
    Then ViewModel has no pending permission
    And the recording "coder" session cancelled pending interactions
    When the recording "reviewer" session requests input "input-1"
    And the ViewModel stops
    Then the recording "reviewer" session cancelled pending interactions

  Scenario: Tool state is visible only while the tool is active
    Given a ViewModel with recording roles "coder"
    When the recording "coder" session emits tool start "git status"
    Then ViewModel role "coder" has active tool "git status"
    When the recording "coder" session emits tool completion "git status"
    Then ViewModel role "coder" has no active tool

  Scenario: Manual prompts are serialized per role
    Given a ViewModel with recording roles "coder"
    When overlapping prompts "first,second" are sent to "coder"
    Then the recording "coder" session received prompts "first,second"
    And the recording "coder" session had no overlapping sends

  Scenario: Slow sends do not block another role
    Given a ViewModel with recording roles "coder,reviewer"
    When a slow prompt is sent to "coder" while a prompt is sent to "reviewer" concurrently
    Then the recording "reviewer" session received prompt "fast"
    And the reviewer prompt completed before the coder prompt

  Scenario: Abort is routed to the matching role
    Given a ViewModel with recording roles "coder,reviewer"
    When the recording "coder" session emits an idle event
    And "coder" is aborted
    Then the recording "coder" session has one abort
    And the recording "reviewer" session has no abort
    And ViewModel role "coder" is not ready for a prompt

  Scenario: Cancelling an active prompt cancels its local operation
    Given a ViewModel with recording roles "coder"
    When a slow prompt "first" starts for "coder"
    And "coder" is aborted
    Then the active prompt was cancelled
    And the recording "coder" session has one abort

  Scenario: A prompt waits for an in-flight cancellation to finish
    Given a ViewModel with recording roles "coder"
    When a prompt "first" is sent to "coder"
    And cancellation starts for "coder"
    And prompt "second" is started while cancellation is pending for "coder"
    Then the pending prompt has not been sent to "coder"
    When cancellation completes for "coder"
    Then the recording "coder" session received prompts "first,second"

  Scenario: Events from a cancelled turn are ignored
    Given a ViewModel with recording roles "coder"
    When "coder" is aborted
    And the recording "coder" session emits a final assistant message "stale response"
    Then ViewModel role "coder" transcript has no entry "stale response"

  Scenario: Repeated idle cancellation is safe
    Given a ViewModel with recording roles "coder"
    When "coder" is aborted
    And "coder" is aborted
    Then the recording "coder" session has two aborts

  Scenario: A failed cancellation can be retried
    Given a ViewModel with recording roles "coder"
    When cancellation fails for "coder"
    Then cancellation failed
    When "coder" is aborted
    And a prompt "next" is sent to "coder"
    Then the recording "coder" session received prompt "next"
    And the recording "coder" session has two aborts