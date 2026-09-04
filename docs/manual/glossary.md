# Glossary

## Squad

The configured set of roles cooperating on one target repository. Its
checked-in definition consists of `blaxquad/squad.json`, the constitution
prompt, and one prompt per role.

## Role

A named squad responsibility configured in `blaxquad/squad.json`. A role binds
together a unique name and worktree, a receive mode, agent settings, and a role
prompt. Each configured worktree belongs to one role; role names may not contain
underscores.

## Constitution prompt

The shared instructions for every role, stored at
`blaxquad/constitution.prompt`. Each agent reads it, including files it
references recursively, before reading its role prompt.

## Role prompt

Role-specific instructions stored at `blaxquad/roles/<role>.prompt`. Each agent
reads this prompt after the constitution prompt, including files referenced by
the prompt recursively.

## Headquarters

The central process that runs and coordinates a squad. It prepares each role's
worktree, starts its Copilot agent session, opens the desktop dashboard, moves
handoffs between roles, and manages startup, relaunch, and shutdown.

The user runs headquarters through the `squad-hq` executable.

## Agent provider

An external system that supplies the agent capabilities including models,
conversation sessions, tool execution, interaction requests, events, and usage data.

Example: GitHub Copilot (SDK/CLI).

## Agent backend

The adapter between headquarters and an agent provider. The current
implementation uses the GitHub Copilot SDK, creates one agent session per role,
and translates provider events and interaction requests into th typed runtime model.

## Worktree

The Git checkout in which a role's agent session operates. The special
configuration value `master` uses the main checkout; any other value creates or
uses a dedicated checkout under `.worktrees/<name>` on branch `squad-<name>`.

Worktrees isolate concurrent role changes, provide role identity to the
`squad` CLI, and hold each role's durable handoff state.

## Dashboard

The Vue application hosted in the Photino desktop window. It renders one panel
per role with status, transcript, model and usage data, pending interactions,
and prompt controls.

The dashboard is a presentation client. C# owns session lifecycle, command
validation, transcripts, handoff delivery, and other authoritative state.

## Agent session

The live conversation and execution channel for one role. A session is bound to
the role's worktree and supports prompts, cancellation, permission responses,
input responses, and elicitation responses.

Sessions are transient. Relaunching or shutting down the squad ends them, while
durable handoff state remains in the worktrees.

## Agent event

A typed observation emitted by an agent session, such as a user or assistant
message, reasoning, tool activity, readiness change, usage update, interaction
request, or failure. Headquarters folds these events into role state and the
role transcript.

## Transcript

The ordered, per-role presentation history derived from typed agent events. It
contains user, assistant, reasoning, tool, system, harness, and error activity,
while omitting provider plumbing that is not meaningful to users.

Recent content is retained in live state for fast updates. Older entries are
available through bounded, paged transcript history; the UI synchronizes by
sequence so reconnects and concurrent streaming do not reorder content.

## Interaction request

A request that pauses an agent operation until a user responds. The dashboard
routes each response back to the role and request that created it. The three
interaction kinds are:

- **Permission request**: approve or reject a proposed operation.
- **Input request**: choose an offered answer or enter free-form text.
- **Elicitation**: respond to a structured form or URL flow.

Pending interactions are canceled when their role is aborted, replaced, or shut
down.

## Elicitation

A structured request from an agent that asks the user to supply information or
complete an external action before the agent continues. Unlike a free-form
input request, an elicitation tells the dashboard what interaction to present:

- A **form elicitation** defines named fields and which are required. For
  example, an agent preparing a deployment could request an environment, a
  version number, and a confirmation checkbox. The dashboard renders the form
  and returns the submitted values to the agent.
- A **URL elicitation** asks the user to open a specific web page. For example,
  a tool could ask the user to complete authentication on its website. The
  dashboard shows the destination and lets the user open it or cancel.

The user may accept, decline, or cancel the request in the dashboard. The
response is sent back to the waiting agent session.

## Permission mode

The role-level `agent.permissions` setting in `blaxquad/squad.json`:

- `prompt` is the default and presents permission requests for user approval.
- `approveAll` automatically approves requests that do not require managed
  approval.

Safe reads inside the role worktree are approved automatically in either mode.
Managed approval requests still require an explicit response.

## Handoff

A validated, durable message from one role to one or more other roles. A sender
creates one with `squad handoff <draft-file>`. On success, the command removes
the draft and places the generated `.handoff` file in the sender's outbox.

The supported handoff types are **Git handoff** and **note handoff**.

## Git handoff

A handoff of type `git_handoff` that identifies a committed change. It carries
a stable task name and an unambiguous 10-character commit abbreviation.
BlaXquad generates a `merge_and_process <sender> <commit>` payload for the
recipient. The handoff communicates the change; it does not itself merge the
commit.

## Note handoff

A handoff of type `note` containing a short message rather than a commit. A note
may target one or several roles and its message is limited to 80 characters.

## Handoff queue

The durable state machine under each role worktree's `.blaxquad/handoffs/` directory:

| Location            | Meaning                                              |
| ------------------- | ---------------------------------------------------- |
| `outbox/`           | Validated handoffs waiting for headquarters delivery |
| `sent/`             | Sender copies that were delivered                    |
| `failed/`           | Sender copies that could not be delivered            |
| `inbox/new/`        | Delivered work not yet claimed by the recipient      |
| `inbox/in_process/` | The recipient's current task or batch                |
| `inbox/completed/`  | Work explicitly completed by the recipient           |

Moving files between these locations is the authoritative queue transition.

## Handoff delivery

The headquarters background service that scans role outboxes. It first
persists a recipient copy in every destination inbox, then archives the sender
copy as sent and wakes each recipient session.

Delivery is file-backed and restart-safe. A retry does not duplicate an
already-persisted recipient copy, and a notification failure does not discard
the delivered handoff.

## Receive mode

The `receiveMode` configured for a role:

- `task` claims at most one handoff at a time and is the default.
- `batch` claims all currently queued handoffs at the best priority.

The generic `squad ready-for-next` and `squad done-with-current` commands select
the correct behavior from the current role's configured receive mode.

## Task receive mode

The default receive mode, in which a role has zero or one handoff in progress.
`squad ready-for-next` resumes the current handoff or claims the best queued
one. `squad done-with-current` archives it and immediately checks for the next
handoff.

## Batch receive mode

A role receive mode that claims every currently queued handoff at the best
available priority as one batch. All claimed items move together into a batch
directory under `inbox/in_process`.

Completing the batch archives all its items and immediately checks for the next
batch.

## Host lease

The exclusive ownership record for one running headquarters instance per
project. It uses `.blaxquad/host.lock`, publishes connection metadata in
`.blaxquad/host.json`, and exposes a local control channel for status, readiness,
and shutdown requests.

The lease prevents two hosts from managing the same project concurrently.

## Session generation

The complete set of role sessions created by one startup or relaunch. A
generation becomes active only after all of its sessions are registered.
Generation identity prevents delayed events from retired sessions from mutating
current role state.

`SessionRegistry` currently only tracks session lookup and shutdown admission;
it is not yet the authoritative generation lifecycle described above. That
lifecycle aggregate is deferred to `restart button.md`.

## Assembly boundaries

The application-domain state is split across a small set of assemblies with a
single, non-cyclic dependency direction:

- **`squad.Core`** owns the serialized application coordinator
  (`SquadViewModel`), role/session status (`AgentRoleState`), session lookup
  (`SessionRegistry`), and the internal `RoleOperations`, `Interactions`, and
  `Events` modules that project provider events and admit commands at one
  event-loop commit boundary. It depends only on the agent provider and UI
  abstractions and on `squad.Core.Transcripts`.
- **`squad.Core.Transcripts`** owns the transcript aggregate: ordering,
  streaming, protection, retention, truncation, paging, and archive storage.
  It depends only on `squad.Ui.Abstractions` and never calls back into
  `squad.Core`.
- **`squad.Core.Handoffs`** owns filesystem-backed handoff polling, recovery,
  and delivery. It depends only on `squad.Agent.Configuration` and
  `squad.Agent.Handoff`, and never references `squad.Core`; it invokes an
  injected role-notification contract by recipient role name only.
- **`squad-hq`** composes the process/runtime lifecycle: it references
  `squad.Core` and `squad.Core.Handoffs` (and, transitively,
  `squad.Core.Transcripts`), hosts the `SessionRoleNotifier` adapter between
  handoff delivery and `SessionRegistry`/`SquadViewModel`, and owns startup,
  relaunch, and shutdown. Neither Core module references `squad-hq`.

## UI protocol

The versioned JSON message boundary between the Photino host and Vue dashboard.
The host publishes snapshots, transcript synchronization, updates, pages,
archived entries, and protocol errors. The dashboard sends readiness, prompt,
abort, relaunch, interaction response, and transcript retrieval commands.

## Snapshot

A point-in-time representation of all role summaries and pending interactions
sent from the C# host to the dashboard. Snapshots establish state; sequenced
transcript synchronization and incremental updates preserve event ordering
between snapshots.
