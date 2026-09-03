# Glossary

## Agent backend

The adapter between headquarters and an agent provider. The current
implementation uses the GitHub Copilot SDK, creates one agent session per role,
and translates provider events and interaction requests into BlaXquad's typed
runtime model.

The configured backend name in `blaxquad/squad.json` is `copilot`.

## Agent event

A typed observation emitted by an agent session, such as a user or assistant
message, reasoning, tool activity, readiness change, usage update, interaction
request, or failure. Headquarters folds these events into role state and the
role transcript.

## Agent session

The live conversation and execution channel for one role. A session is bound to
the role's worktree and supports prompts, cancellation, permission responses,
input responses, and elicitation responses.

Sessions are transient. Relaunching or shutting down the squad ends them, while
durable handoff state remains in the worktrees.

## AI credits (AIC)

The provider-reported usage accumulated by an agent session. The dashboard
shows the value when the SDK supplies it and reports it as unavailable
otherwise.

## Batch receive mode

A role receive mode that claims every currently queued handoff at the best
available priority as one batch. All claimed items move together into a batch
directory under `inbox/in_process`.

Completing the batch archives all its items and immediately checks for the next
batch.

## Constitution prompt

The shared instructions for every role, stored at
`blaxquad/constitution.prompt`. Each agent is instructed to read it, including
files it references recursively, before reading its role prompt.

## Context usage

The number of tokens currently attributed to a session compared with the
selected model's context limit. The dashboard displays this as a meter when the
SDK can resolve both values.

## Continue launch

`squad-hq launch --continue` starts the squad without resetting existing
dedicated worktrees or clearing handoff state. Use it to resume work from a
previous run.

A normal `squad-hq launch` resets dedicated worktrees to the repository's
current `HEAD` and clears configured handoff queues before starting.

## Dashboard

The Vue application hosted in the Photino desktop window. It renders one panel
per role with status, transcript, model and usage data, pending interactions,
and prompt controls.

The dashboard is a presentation client. C# owns session lifecycle, command
validation, transcripts, handoff delivery, and other authoritative state.

## Elicitation

A structured request from an agent for user-supplied information. BlaXquad
supports form elicitations and URL elicitations, and lets the user accept,
decline, or cancel them in the dashboard.

## Execution context

The identity resolved by the `squad context` command from the current Git
worktree. It includes the role, project root, role worktree root, and shared
source path. Role identity does not depend on a role environment variable.

## Git handoff

A handoff of type `git_handoff` that identifies a committed change. It carries
a stable task name and an unambiguous 10-character commit abbreviation.

BlaXquad generates a `merge_and_process <sender> <commit>` payload for the
recipient. The handoff communicates the change; it does not itself merge the
commit.

## Handoff

A validated, durable message from one role to one or more other roles. A sender
creates one with `squad handoff <draft-file>`. On success, the command removes
the draft and places the generated `.handoff` file in the sender's outbox.

The supported handoff types are **Git handoff** and **note handoff**.

## Handoff delivery

The headquarters background service that scans role outboxes. It first
persists a recipient copy in every destination inbox, then archives the sender
copy as sent and wakes each recipient session.

Delivery is file-backed and restart-safe. A retry does not duplicate an
already-persisted recipient copy, and a notification failure does not discard
the delivered handoff.

## Handoff queue

The durable state machine under each role worktree's
`.blaxquad/handoffs/` directory:

| Location | Meaning |
|---|---|
| `outbox/` | Validated handoffs waiting for headquarters delivery |
| `sent/` | Sender copies that were delivered |
| `failed/` | Sender copies that could not be delivered |
| `inbox/new/` | Delivered work not yet claimed by the recipient |
| `inbox/in_process/` | The recipient's current task or batch |
| `inbox/completed/` | Work explicitly completed by the recipient |

Moving files between these locations is the authoritative queue transition.

## Headquarters (`squad-hq`)

The operator-facing executable and runtime composition root. It reads the
squad configuration, prepares worktrees, acquires host ownership, opens the
desktop UI, starts the Copilot backend and role sessions, delivers handoffs,
and coordinates relaunch and shutdown.

The main commands are `squad-hq launch`, `squad-hq shutdown`, and
`squad-hq wait-for-agent`.

## Host lease

The exclusive ownership record for one running headquarters instance per
project. It uses `.blaxquad/host.lock`, publishes connection metadata in
`.blaxquad/host.json`, and exposes a local control channel for status, readiness,
and shutdown requests.

The lease prevents two hosts from managing the same project concurrently.

## Interaction request

A request that pauses an agent operation until a user responds. The dashboard
routes each response back to the role and request that created it. The three
interaction kinds are:

- **Permission request**: approve or reject a proposed operation.
- **Input request**: choose an offered answer or enter free-form text.
- **Elicitation**: respond to a structured form or URL flow.

Pending interactions are canceled when their role is aborted, replaced, or
shut down.

## Manual prompt

A prompt sent by a user from a dashboard role panel to that role's existing
session. Prompts are serialized within one role, while different roles can
continue independently.

## Note handoff

A handoff of type `note` containing a short message rather than a commit. A note
may target one or several roles and its message is limited to 80 characters.

## Permission mode

The role-level `agent.permissions` setting in `blaxquad/squad.json`:

- `prompt` is the default and presents permission requests for user approval.
- `approveAll` automatically approves requests that do not require managed
  approval.

Safe reads inside the role worktree are approved automatically in either mode.
Managed approval requests still require an explicit response.

## Priority

A two-digit handoff value from `00` through `99`. Lower numbers are processed
first, so `10` has priority over `50`.

Task roles claim the first queued item at the best priority. Batch roles claim
all currently queued items with that same priority.

## Readiness

A role is ready when its current session is idle, has no active work, and
headquarters is accepting commands. `squad-hq wait-for-agent <role>` waits on
this host-owned state instead of guessing from process existence.

## Receive mode

The `receiveMode` configured for a role:

- `task` claims at most one handoff at a time and is the default.
- `batch` claims all currently queued handoffs at the best priority.

The generic `squad ready-for-next` and `squad done-with-current` commands select
the correct behavior from the current role's configured receive mode.

## Recovery

The ability to resume durable work after a command or host restart. A current
task remains in `inbox/in_process`, queued work remains in `inbox/new`, and
headquarters wakes roles that already have pending inbox work when sessions
start again.

## Relaunch

A live replacement of every agent session without restarting the headquarters
process or desktop shell. Headquarters stops handoff polling, retires the old
session generation, clears transient session state, starts a complete
replacement generation, recovers inbox notifications, and resumes delivery.

Relaunch does not reset worktrees or discard durable handoffs.

## Role

A named squad responsibility configured in `blaxquad/squad.json`. A role binds
together:

- a unique name and worktree;
- a receive mode;
- an agent backend, permission mode, optional model, and optional reasoning
  effort;
- a role prompt.

Role names may not contain underscores. Each configured worktree belongs to one
role.

## Role prompt

Role-specific instructions stored at `blaxquad/roles/<role>.prompt`. Each agent
reads this prompt after the constitution prompt, including files referenced by
the prompt recursively.

## Runtime state

Generated, untracked state stored under `.blaxquad/`. The project root contains
host metadata and the delivery log; each role worktree contains its handoff
queues. `.worktrees/` contains the dedicated Git worktrees.

This is distinct from the checked-in `blaxquad/` directory, which contains the
squad's configuration and prompts.

## Session generation

The complete set of role sessions created by one startup or relaunch. A
generation becomes active only after all of its sessions are registered.

Generation identity prevents delayed events from retired sessions from
mutating current role state.

## Shared worktree path

A repository-relative directory listed in `sharedWorktreePaths` in
`blaxquad/squad.json`. Headquarters keeps the directory in the main checkout
and links the corresponding location in every dedicated worktree to it, using
a junction on Windows or a symbolic link on other platforms.

Shared paths must remain inside the repository and may not overlap each other.

## Snapshot

A point-in-time representation of all role summaries and pending interactions
sent from the C# host to the dashboard. Snapshots establish state; sequenced
transcript synchronization and incremental updates preserve event ordering
between snapshots.

## Squad

The configured set of roles cooperating on one target repository. Its
checked-in definition consists of `blaxquad/squad.json`, the constitution
prompt, and one prompt per role.

## `squad` CLI

The role-side helper executable available inside agent sessions. It provides
commands for creating handoffs, resolving execution context, claiming queued
work, and completing current work.

Unlike `squad-hq`, it does not own the application lifecycle or desktop UI.

## Task receive mode

The default receive mode, in which a role has zero or one handoff in progress.
`squad ready-for-next` resumes the current handoff or claims the best queued
one. `squad done-with-current` archives it and immediately checks for the next
handoff.

## Transcript

The ordered, per-role presentation history derived from typed agent events. It
contains user, assistant, reasoning, tool, system, harness, and error activity,
while omitting provider plumbing that is not meaningful to users.

Recent content is retained in live state for fast updates. Older entries are
available through bounded, paged transcript history; the UI synchronizes by
sequence so reconnects and concurrent streaming do not reorder content.

## UI protocol

The versioned JSON message boundary between the Photino host and Vue
dashboard. The host publishes snapshots, transcript synchronization, updates,
pages, archived entries, and protocol errors. The dashboard sends readiness,
prompt, abort, relaunch, interaction response, and transcript retrieval
commands.

## Worktree

The Git checkout in which a role's agent session operates. The special
configuration value `master` uses the main checkout; any other value creates or
uses a dedicated checkout under `.worktrees/<name>` on branch
`squad-<name>`.

Worktrees isolate concurrent role changes, provide role identity to the
`squad` CLI, and hold each role's durable handoff state.
