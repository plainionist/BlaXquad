# Modules

See [Current architecture](architecture.md) for the system context, runtime containers, component relationships,
state ownership, and main execution flows.

Each C# project in `squad.slnx` produces one module (assembly). The Vue
dashboard in `src/squad-ui` is not included because it is a frontend package,
not a .NET assembly.

## `squad-hq`

Headquarters executable and composition root. Implements `launch`, `shutdown`,
and `wait-for-agent`, and wires workspace preparation, Headquarters control, the
Copilot backend, handoff delivery, application state, and the desktop UI.

Once the Headquarters lease is acquired, `launch` creates and owns the process's single diagnostic log
file (see [Diagnostic log](glossary.md#diagnostic-log)) for the rest of the launch, and registers the
launch boundary's last-chance `AppDomain.UnhandledException`/`TaskScheduler.UnobservedTaskException`
handlers alongside it.

## `squad`

Agent-facing command-line executable. Resolves the current role and worktree,
exposes context, validates and queues handoffs, and advances task or batch inbox
state through `ready-for-next` and `done-with-current`.

## `squad.AgentProvider.Abstractions`

Defines the provider-neutral agent boundary: backend, runtime, and session
lifecycle contracts; role context; typed agent events; and interaction
responses.

## `squad.AgentProvider.CopilotSdk`

Adapts the GitHub Copilot SDK to the provider-neutral contracts. It starts and
stops the provider runtime, manages role sessions and interactions, reports
usage and readiness, and normalizes provider and tool events.

## `squad.AgentProvider.Fake`

Standalone fake agent-provider adapter loaded by backend acceptance
specifications through the same explicit `--provider` descriptor a real
provider uses. Exposes only the reflection-loaded fixture factory types the
production loader needs; its fake runtime, sessions, event stream, and
private control transport stay internal, with `squad.Specs` granted access
through `InternalsVisibleTo`.

## `squad.Application`

Owns authoritative live per-member state and user operations, including the active session catalog and command
admission. One squad generation holds the ordered member directory, its command admission, and the strong
generation identity every member and message carries; a process-lifetime facade above it forwards each command and
query to whichever generation is installed, rejects anything addressed to a retired one, and answers as an empty
squad while none is installed. Each configured squad member has one member aggregate that is the sole mutable
owner of that member's projected agent status, transcript, pending interactions, and operation/abort coordination -
keyed only by request or operation identity, never by another member - and one member processor: a bounded,
single-reader mailbox that is the sole path through which that member's prompt, abort, interaction-response, and
provider-event commands reach its aggregate, so one member's provider I/O can never delay another member's mailbox.
It projects agent events onto the addressed member's aggregate, coordinates prompts and aborts, integrates
transcript state, and supplies UI snapshots composed from immutable member snapshots. Member status is carried as
`squad.Domain`'s `SquadMemberStatus` enum throughout this authoritative state; it is mapped to the stable lowercase
`state.snapshot` spelling only where that snapshot is composed. Its `Transcripts` component
owns per-member transcript state, including ordered entries, streaming buffers, tool-call correlation, live
retention limits, durable archives, paging, and archived-entry reconstruction; the archive itself, its retention
policy, and each member's monotonic publication identity live for the process, and a generation reaches them only
through a bounded handle revoked when it retires.

## `squad.Configuration`

Locates project and worktree context, loads and validates
`blaxquad/squad.json`, models role and agent settings, and resolves the role
associated with the current worktree.

## `squad.Domain`

The dependency-free shared kernel of stable, immutable squad vocabulary consumed by every other module: `RoleId`
and `SquadMemberId` (distinct reusable-role and unique-member identities), `ReceiveMode` (a member's `Task` or
`Batch` handoff acceptance mode), `PermissionMode` (a member's `Prompt` or `ApproveAll` agent permission
behavior), `SquadMemberStatus` (a member's `Starting`/`Running`/`Idle`/`Stopped`/`Error` lifecycle status),
`AgentSettings` (normalized permission mode, model, and effort), `SquadMemberDefinition` (one
resolved member's identity, display name, role reference, worktree, receive mode, and agent settings), and
`SquadDefinition` (the ordered member roster and leader identity), plus the foundational `System.Contract` guard
utility. `Contract.Requires` expresses a semantic caller obligation the type system cannot express (a bounded
range, a supported variant, a required capability); `Contract.Invariant` expresses a state that must be impossible
once inputs have already crossed their validation boundary (a stable ownership, identity, ordering, capacity, or
lifecycle rule). Neither replaces validation of untyped input (JSON, CLI arguments, handoff files, reflection,
filesystem, or process boundaries), and neither should be used to null-guard a non-nullable reference parameter on
a trusted typed call path: nullable annotations enforced as warnings-as-errors already own that obligation there.
It has no project references; configuration parsing, JSON, filesystem, and protocol concerns stay in the
modules that map external input onto these values.

## `squad.Handoffs`

Provides shared file-backed handoff primitives: the typed JSON document
model, centralized serializer options and validation, priority formatting,
sequence and timestamp generation, and queue entry listing and rendering.
Every Headquarters launch discards each configured worktree's complete
handoff-state directory - legacy `.handoff` artifacts included - before role
commands can run against it, so no legacy-queue guard is needed. Its
`Delivery` submodule runs Headquarters-side handoff delivery: it polls role
outboxes, durably writes recipient inbox copies, archives sent or failed
items, and wakes recipient sessions. A delivery failure or a failed-artifact
archival failure is recorded as an error with the handoff path and full
exception, and a recipient notification failure is recorded as a warning with
member and handoff context, into the launch's diagnostic log (see
[glossary.md#diagnostic-log](glossary.md#diagnostic-log)) rather than a
separate fixed-purpose delivery log; a successful delivery adds no
warning/error entry.

## `squad.Hosting.Abstractions`

Defines the narrow platform-hosting contracts for the desktop window lifecycle
and system sleep inhibition, plus the `IHostingFactory` runtime-loading contract
(`HostingContext` in, `HostingRuntime` out) that `squad-hq` uses to load a
hosting adapter from an explicit `--hosting <assemblyPath>;<typeName>`
descriptor, mirroring how it already loads agent providers.

## `squad.Hosting.Stdio`

Implements `IHostingFactory` with a headless, stdio-framed `IWindowHost` that
drives a `UiProtocolSession` over standard input/output. It is test-distributed
only: it builds beside `squad.Specs` and is loaded at runtime via `--hosting`,
never referenced or built into production `squad-hq` packaging.

## `squad.Hosting.Fake`

Standalone fixture project exercising `--hosting` descriptor loading failure
modes: an incompatible-type fixture, a factory whose `Create` throws, and a
factory with no public parameterless constructor. Loaded by backend
acceptance specifications the same way a real hosting adapter is.

## `squad.Hosting.Photino`

Implements `IHostingFactory` with Photino and platform-specific sleep
prevention, hosting the built Vue dashboard and carrying the UI protocol over
native web messages. `squad-hq` never references it at compile time; its
`Photino.Publish.targets` runs a nested build/publish of this project and
copies its assembly, dependency manifest, managed and native dependencies,
built Vue distribution, and window icon into both `squad-hq`'s ordinary build
output and its publish output, so `--hosting` resolves it as the packaged
default exactly like an explicit descriptor resolves any other adapter.

## `squad.HeadquarterTools`

Implements the workspace tools squad-hq discovers or launches on the operator's behalf, grouped by folder.
`Issues/` discovers and parses the fixed workspace `docs/issues` Markdown catalog: it splits and parses YAML
frontmatter, resolves title and priority fallbacks independently, extracts a bounded body preview, normalizes
workspace-relative paths, and orders the catalog by ascending priority and filename. `History/` implements
`IWorkspaceTools` (`squad.Ui.Abstractions`) as the optional Git history launcher: it resolves the configured
`gitHistoryCommand` executable once at startup through `squad.Process`'s executable discovery, reports whether
it is available, and launches it detached in the workspace root when invoked.

## `squad.Process`

Supplies shared process and CLI infrastructure: executable discovery,
synchronous and asynchronous child-process execution, output capture,
cancellation, result values, and exit-code exceptions.

## `squad.Runtime`

Separates the headquarters process shell from the replaceable squad generation
after composition. The shell sequences startup and cleanup, owns the window,
sleep, transcript-archive, and workspace-tool resources, and holds one
serialized active-squad slot. One squad generation owns the provider backend,
the member directory and its processors, command admission, started sessions
and their event and failure observers, and handoff-pump participation; it is
started through one start operation and released through one idempotent,
failure-collecting retirement, and a retirement that cannot conclude keeps its
generation owned so no replacement can overlap it. Exactly one generation is
installed today, at startup, and retired once, at shutdown. Its `Control`
component enforces one headquarters process per project and provides local
process control: it owns the Headquarters lock and metadata, named-pipe
shutdown and readiness requests, client access, and stale-state cleanup.
Control code remains structurally separate from lifecycle coordination within
the same assembly.

Its session-observation component (`SessionGeneration`) also records a provider error event and an unexpected
event-stream or session-completion failure - each with the member ID - into the launch-owned diagnostic log (see
[Diagnostic log](glossary.md#diagnostic-log)) before that failure becomes the member's visible error state on the
dashboard. Expected session cancellation during Headquarters shutdown is not recorded.

## `squad.Ui.Abstractions`

Defines transport-neutral contracts and data exchanged between application
state and presentation: user commands, snapshots, transcript announcements and
updates, pages, and archived entries. Also defines `IWorkspaceTools`, the
narrow, workspace-scoped contract each configured tool (for example the
optional Git history launcher in `squad.HeadquarterTools`) implements to report its own
availability and perform its own launch.

## `squad.Ui.Protocol`

Owns the internal JSON protocol between Headquarters and dashboard: envelope
validation, command routing, snapshot publication, transcript sequencing and
journaling, synchronization, recovery, and protocol errors.

## `squad.Workspaces`

Builds launch context and prepares repository workspaces in two distinct
lifetimes. Process preparation runs once: it initializes Git state, parses role
configuration, creates or resets worktrees, links shared paths, writes agent
instructions, and creates runtime and handoff directories. Generation
preparation reloads the current configuration and role prompts and builds the
backend and the one ordered `SquadDefinition` a squad generation is created
from, touching no worktree and no durable handoff queue.
