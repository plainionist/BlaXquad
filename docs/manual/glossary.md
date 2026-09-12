# Glossary

## Squad

The configured group of uniquely named squad members, each referencing a reusable role, cooperating on one target
repository. Its checked-in definition consists of `blaxquad/squad.json`, the constitution prompt, and one prompt
per role.

## Role

A reusable, named responsibility declared in `blaxquad/squad.json`'s `roles` catalog and instructed by one role
prompt. A role is not itself a runtime participant and owns no worktree, receive mode, or agent settings of its
own: one or more squad members may reference the same role.

## Squad member

A uniquely named, configured participant declared in `blaxquad/squad.json`'s `members` array. A member binds
together its own name, the role it references, a worktree, a receive mode, agent settings, and a display name.
Each configured worktree belongs to one member; member names may not contain underscores. The existing `squad` CLI,
handoff documents, and UI protocol continue to address a member through the same string fields they have always
called "role".

## Constitution prompt

The shared instructions for every role, stored at
`blaxquad/constitution.prompt`. Each agent reads it, including files it
references recursively, before reading its role prompt.

## Role prompt

Role-specific instructions stored at `blaxquad/roles/<role>.prompt`. Each agent
reads this prompt after the constitution prompt, including files referenced by
the prompt recursively.

## Headquarters

The central process that runs and coordinates a squad. It prepares each member's
worktree, starts its Copilot agent session, opens the desktop dashboard, moves
handoffs between members, and manages startup and shutdown.

The user runs headquarters through the `squad-hq` executable.

## Agent provider

An external system that supplies the agent capabilities including models,
conversation sessions, tool execution, interaction requests, events, and usage data.

Example: GitHub Copilot (SDK/CLI).

## Agent backend

The adapter between headquarters and an agent provider. The current
implementation uses the GitHub Copilot SDK, creates one agent session per squad
member, and translates provider events and interaction requests into the typed runtime model.

## Worktree

The Git checkout in which one squad member's agent session operates. The special
configuration value `master` uses the main checkout; any other value creates or
uses a dedicated checkout under `.worktrees/<name>` on branch `squad-<name>`.

Worktrees isolate concurrent member changes, provide role identity to the
`squad` CLI, and hold each member's durable handoff state.

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
message, reasoning, tool activity, idle transition, usage update, interaction
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

The squad member's `agent.permissions` setting in `blaxquad/squad.json`:

- `prompt` is the default and presents permission requests for user approval.
- `approveAll` automatically approves requests that do not require managed
  approval.

Safe reads inside the member's worktree are approved automatically in either mode.
Managed approval requests still require an explicit response.

## Handoff

A validated, durable message from one role to one or more other roles. A sender
creates one directly with `squad handoff commit` or `squad handoff note` - there
is no intermediate draft file. On success, the command places the generated
`.handoff.json` file - a typed JSON document - in the sender's outbox. The
document records identity, sender, recipients as a
JSON array, priority as a number, its kind and kind-specific data, and
lifecycle timestamps; it does not persist the recipient-facing payload text,
which is derived from the typed kind-specific data whenever a handoff is
displayed or delivered.

The supported handoff types are **Git handoff** and **note handoff**.

## Git handoff

A handoff of type `git_handoff` that identifies a committed change, created
with `squad handoff commit --to <role>[,<role>...] --task <task>`. It carries
a stable task name and an unambiguous 10-character commit abbreviation. The
command resolves `HEAD` (or an explicit `--commit <revision>`) to a commit and
persists Git's canonical ten-character abbreviation; a default `HEAD` handoff
requires a clean worktree. BlaXquad generates a `merge_and_process <sender>
<commit>` payload for the recipient. The handoff communicates the change; it
does not itself merge the commit.

## Note handoff

A handoff of type `note` containing a short message rather than a commit,
created with `squad handoff note --to <role>[,<role>...] --message <message>`.
A note may target one or several roles and its message is limited to 80
characters.

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

Moving files between these locations is the authoritative queue transition within one Headquarters run.

Handoff queues are launch-scoped, not restart-safe: every launch, continued or
not, discards each configured worktree's complete handoff-state directory -
every queued, in-process, completed, sent, and failed handoff, including
nested batch directories - before any role session starts or delivery polling
runs. `--continue` preserves Git worktree content only; it never preserves
queued handoffs. A legacy, pre-JSON `.handoff` artifact left by an old release
is discarded the same way, with no migration or guard needed.

## Handoff delivery

The headquarters background service that scans role outboxes. It first
persists a recipient copy in every destination inbox, then archives the sender
copy as sent and wakes each recipient session.

Delivery is file-backed and idempotent within one Headquarters run: a retry
does not duplicate an already-persisted recipient copy, and a notification
failure does not discard the delivered handoff. This durability does not
cross a Headquarters launch: every launch discards the queue first.

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

## Headquarters lease

The exclusive ownership record for one running headquarters instance per
project. It uses `.blaxquad/host.lock`, publishes connection metadata in
`.blaxquad/host.json`, and exposes a local control channel for status, readiness,
and shutdown requests.

The lease prevents two Headquarters instances from managing the same project concurrently.

## Session generation

The complete set of role sessions created by one headquarters startup. A
generation becomes active only after all of its sessions are registered, the UI
has been notified, and the handoff poller has started.

`SquadMembers` is one generation's command-admission authority and the sole
owner of the active-session catalog: one synchronization boundary admits a
command and captures its role's current session together. `SessionGeneration`
separately owns the provider runtime handle and the event/completion observers
for one generation, registering each started session directly into that
generation's member directory as it starts. Both belong to the `Squad` that
owns the generation, and the process-lifetime `SquadViewModel` facade forwards
commands to whichever `Squad` is currently installed.

## UI protocol

The internal JSON message boundary between Headquarters and the Vue dashboard, shared by both as one packaged
product. Message envelope parsing, serialization, command routing, snapshot scheduling, transcript delivery,
journaling, and recovery are owned by `squad.Ui.Protocol`.
`squad.Hosting.Photino` is only the native window and sleep-inhibition adapter that
carries this protocol over the OS window; it has no envelope, delivery, or
recovery logic of its own. Headquarters never references it at compile time - it
is the packaged default hosting plug-in, runtime-loaded through the same
`--hosting` mechanism as any other adapter when the option is omitted. Headquarters publishes snapshots, transcript
synchronization, updates, pages, archived entries, protocol errors, and - once, during the `ui.ready` handshake - a
`workspace-tools.snapshot` reporting each configured workspace tool's availability (for example
`gitHistoryAvailable`). The
dashboard sends readiness, prompt, abort, interaction response, transcript retrieval, and workspace-tool
commands (for example `git-history.open`).

## Snapshot

A point-in-time representation of all role summaries and pending interactions
sent from Headquarters to the dashboard. Snapshots establish state; sequenced
transcript synchronization and incremental updates preserve event ordering
between snapshots.
