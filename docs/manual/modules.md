# Modules

See [Current architecture](architecture.md) for the system context, runtime containers, component relationships,
state ownership, and main execution flows.

Each C# project in `squad.slnx` produces one module (assembly). The Vue
dashboard in `src/squad-ui` is not included because it is a frontend package,
not a .NET assembly.

## `squad-hq`

Headquarters executable and composition root. Implements `launch`, `shutdown`,
and `wait-for-agent`, and wires workspace preparation, host control, the
Copilot backend, handoff delivery, application state, and the desktop UI.

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

Owns authoritative live role state and user operations, including the active
role-session catalog and command admission. It projects agent events,
coordinates prompts, aborts, and pending interactions, integrates transcript
state, and supplies UI snapshots.

## `squad.Configuration`

Locates project and worktree context, loads and validates
`blaxquad/squad.json`, models role and agent settings, and resolves the role
associated with the current worktree.

## `squad.Handoffs`

Provides shared file-backed handoff primitives: header and body parsing,
priority validation, sequence and timestamp generation, and queue entry listing
and rendering.

## `squad.Handoffs.Delivery`

Runs host-side handoff delivery. It polls role outboxes, durably writes
recipient inbox copies, archives sent or failed items, recovers pending
notifications, and wakes recipient sessions.

## `squad.Host.Control`

Enforces one headquarters process per project and provides local process
control. It owns the host lock and metadata, named-pipe shutdown and readiness
requests, client access, and stale-state cleanup.

## `squad.Host.Runtime`

Coordinates the headquarters lifecycle after composition. It sequences startup
and cleanup, owns the provider-runtime generation, registers started sessions
into the application model, observes session events and failures, and starts
and stops handoff, window, and sleep resources.

## `squad.Hosting.Abstractions`

Defines the narrow platform-hosting contracts for the desktop window lifecycle
and system sleep inhibition, plus the `IHostingFactory` runtime-loading contract
(`HostingContext` in, `HostingRuntime` out) that `squad-hq` uses to load a
hosting adapter from an explicit `--hosting <assemblyPath>;<typeName>`
descriptor, mirroring how it already loads agent providers.

## `squad.Hosting.Stdio`

Implements `IHostingFactory` with a headless, stdio-framed `IWindowHost` that
drives a `UiProtocolSession` over standard input/output. It is test-distributed
only: it ships beside `squad.Specs`' published tools and is loaded at runtime
via `--hosting`, never referenced or built into production `squad-hq` packaging.

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

## `squad.Issues`

Discovers and parses the fixed workspace `docs/issues` Markdown catalog. It splits and parses YAML frontmatter,
resolves title and priority fallbacks independently, extracts a bounded body preview, normalizes workspace-relative
paths, and orders the catalog by ascending priority and filename.

## `squad.Process`

Supplies shared process and CLI infrastructure: executable discovery,
synchronous and asynchronous child-process execution, output capture,
cancellation, result values, and exit-code exceptions.

## `squad.Transcripts`

Owns per-role transcript state, including ordered entries, streaming buffers,
tool-call correlation, live retention limits, durable archives, paging, and
archived-entry reconstruction.

## `squad.Ui.Abstractions`

Defines transport-neutral contracts and data exchanged between application
state and presentation: user commands, snapshots, transcript announcements and
updates, pages, and archived entries.

## `squad.Ui.Protocol`

Owns the versioned JSON protocol between the host and dashboard: envelope
validation, command routing, snapshot publication, transcript sequencing and
journaling, synchronization, recovery, and protocol errors.

## `squad.Workspaces`

Builds launch context and prepares repository workspaces. It initializes Git
state, parses role configuration, creates or resets worktrees, links shared
paths, writes agent instructions, and creates runtime and handoff directories.
