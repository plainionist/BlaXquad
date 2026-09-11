---
title: Use standard formats for structured files
priority: 55
---

# Use standard formats for structured files

## Problem

BlaXquad should not own parsing and rendering code for a private structured-file grammar when an established data
format and maintained parser already fit the job. Custom grammars add escaping rules, malformed-input behavior,
field conversion, and compatibility concerns that the application must otherwise design and maintain itself.

Handoffs are the remaining production example. The direct `squad handoff commit` and `squad handoff note` commands
removed the agent-authored draft format, but they deliberately retained the durable representation. The role tool
still renders a custom `field: value` header block plus a blank-line-separated payload into the sender's outbox.
Headquarters parses and renders that format during fan-out, and role commands scan and rewrite individual headers
as files move through inbox states.

The CLI is the handoff creation API; the filesystem remains the durable transport. Replacing the serialization
format must not replace the outbox pattern, queue directories, atomic publication, fan-out, retry, archival, or
notification behavior.

The current format has no escaping model. Delimiters, duplicate fields, embedded line endings, and future compound
values all require application-specific handling. Recipients are encoded as one comma-separated scalar, priority
is encoded as text, and metadata updates rewrite selected lines. Since handoff artifacts are now generated and
consumed only by BlaXquad, human-authoring convenience is no longer a reason to retain this grammar.

## Goal

Adopt this rule for application-owned persisted data:

- structured machine-owned files use a standard serialization format and maintained parser;
- JSON and `System.Text.Json` are the default when BlaXquad owns both producer and consumer;
- human-authored files may use another established format when a maintained parser handles their structured data;
- opaque prose, diagnostic text, lock files, and single scalar values do not become JSON merely to satisfy the
  rule; and
- filesystem layout and atomic file operations may remain domain-specific because they represent queue and
  ownership semantics, not serialization grammars.

Migrate durable handoff documents to a typed, versioned JSON representation and remove the custom handoff header,
body separator, persisted recipient-list encoding, parsing, rendering, and field-mutation code.

## Persisted-format audit

No other production-parsed file currently requires migration:

| Data                                          | Current format and parser                                                   | Action           |
| --------------------------------------------- | --------------------------------------------------------------------------- | ---------------- |
| `blaxquad/squad.json`                         | JSON through `System.Text.Json`                                             | Keep             |
| `.blaxquad/host.json`                         | JSON through `System.Text.Json`                                             | Keep             |
| transcript entry metadata                     | JSON through `System.Text.Json`; transcript content is opaque UTF-8 text    | Keep             |
| raw SDK event traces                          | JSON Lines through `System.Text.Json`                                       | Keep             |
| `docs/issues/*.md` metadata                   | conventional Markdown YAML frontmatter through YamlDotNet                   | Keep             |
| handoff queue artifacts                       | custom headers, comma-separated recipients, and a blank-line-separated body | Migrate to JSON  |
| handoff sequence counter                      | one invariant-culture integer protected by a lock file                      | Keep as a scalar |
| prompts and issue bodies                      | opaque text consumed as text                                                | Keep             |
| `.gitignore`                                  | external standard format; BlaXquad only ensures required entries exist      | Keep             |
| handoff delivery log and `.error` diagnostics | append-only or opaque text, not parsed by production code                   | Keep             |

The Markdown frontmatter splitter only identifies the conventional `---` envelope; YamlDotNet parses the structured
content. Do not add a Markdown dependency solely to replace that small framing operation unless the application
later needs general Markdown parsing.

## Required design

### Define one typed handoff document

Own one serialized handoff model in `squad.Handoffs` and use it from the role CLI, Headquarters delivery, and role
queue commands. It must represent all currently persisted information, including:

- a schema version;
- handoff identity and sender;
- recipients as a JSON array rather than a comma-separated string;
- the selected recipient on a delivered copy;
- priority as a number, formatted separately when lexical filename ordering or CLI output requires two digits;
- handoff kind and the fields required by its Git-handoff or note variant; and
- created, enqueued, dequeued, and completed timestamps.

Use `System.Text.Json` for every read and write. Centralize serializer options and schema validation with the model;
do not replace the existing grammar with hand-written `JsonDocument` property probing, string concatenation, or
multiple subtly different serializers. Reject malformed documents, unsupported schema versions, missing required
properties, invalid handoff-kind combinations, and unexpected properties with controlled diagnostics appropriate
to the current queue operation.

The exact C# type shape may vary, but keep one top-level type per file and preserve the distinction between a Git
handoff and a note. Derive the existing recipient-facing payload text from those typed fields when displaying a
task instead of persisting the same information again in a second embedded mini-language.

### Preserve the durable queue

Keep `.blaxquad/handoffs/` and its `outbox`, `sent`, `failed`, `inbox/new`, `inbox/in_process`, and
`inbox/completed` states. JSON changes the content representation only.

Use a JSON-identifying suffix such as `.handoff.json`, and retain filename fields needed for stable priority and
creation ordering. Continue to publish files by writing a temporary sibling and atomically moving it into place.
Recipient fan-out must still persist every recipient copy before archiving the sender copy, retries must remain
idempotent, and notification must remain best effort after durable delivery.

Lifecycle metadata updates must deserialize one document, update the typed value, and atomically serialize the
replacement. Remove `HandoffHeaders`, delivery-side `ParseMessage`/`RenderMessage`, and all direct searches or
rewrites of `field: value` lines once no production path needs them.

### Make compatibility finite

Existing queue files can survive a continued launch, so the implementation must make compatibility explicit.
Do not silently ignore legacy `.handoff` files and do not retain an indefinite dual-format reader that defeats the
purpose of this issue.

If compatibility with existing queues is required, provide a bounded, atomic migration for every queue state and
state when the legacy parser will be removed. Otherwise fail clearly before processing a mixed or legacy queue and
document that queues must be drained or reset before upgrading. A normal queue operation must never contain an
ambiguous mixture of legacy and JSON artifacts.

### Keep tests at the behavior boundary

Update the black-box Gherkin mailbox support and handoff scenarios to observe JSON-backed artifacts through the
real `squad` and `squad-hq` processes. Preserve coverage for creation, priority ordering, multi-recipient fan-out,
delivery retry, recovery, task and batch claiming, completion, malformed input, and atomic state transitions.

Test helpers may deserialize JSON independently to observe durable state. They must not reuse production parsing
code or assert serializer formatting, property order, whitespace, or other details that are not part of the
supported schema.

## Acceptance criteria

- Every newly created handoff artifact is valid JSON and is read and written through `System.Text.Json`.
- One shared typed handoff model owns the persisted schema and includes an explicit schema version.
- Recipients and numeric values use native JSON types rather than encoded delimiter-separated strings.
- No production code parses, renders, searches, or mutates the legacy handoff header/body grammar.
- `HandoffHeaders` and delivery-local handoff parsing/rendering are removed.
- Outbox publication and every inbox/sent/failed queue transition remain file-backed and atomic.
- Multi-recipient delivery remains durable and idempotent, and lifecycle timestamps survive each rewrite.
- Malformed JSON, unsupported versions, and invalid handoff variants produce controlled failures without losing the
  source artifact.
- Legacy queue compatibility follows one documented, finite policy and mixed-format queues cannot be processed
  partially.
- The manual describes JSON as an internal durable representation while keeping the CLI as the creation API and
  filesystem moves as the authoritative queue transitions.
- Existing black-box handoff, delivery, recovery, task, and batch scenarios pass with JSON artifacts, with focused
  scenarios added for malformed/version-incompatible documents and any supported migration path.
- A production-source search finds no other application-owned structured file parsed by a custom grammar; any new
  finding is either migrated in this issue or recorded in the audit above with a concrete reason to remain plain
  text or use its existing standard parser.

## Non-goals

- Replacing the filesystem queue with direct process calls, a database, or a network service.
- Changing handoff command syntax or asking agents to author JSON.
- Converting plain prompts, issue prose, scalar counters, lock files, or diagnostics into structured documents.
- Replacing YAML frontmatter or YamlDotNet with JSON.
- Standardizing external command output, filenames, named-pipe framing, or other data that is not an
  application-owned persisted-file grammar.

## Implementation plan

### Architectural decisions

- Use schema version `1` and a discriminated typed document owned by `squad.Handoffs`. The envelope contains the
  identity, sender, recipient array, optional selected recipient, numeric priority, kind, and lifecycle timestamps;
  kind-specific typed data contains either the Git task and commit or the note message. Do not persist a derived
  recipient-facing payload.
- Centralize strict `System.Text.Json` options, validation, reading, and atomic sibling-file writing in
  `squad.Handoffs`. Reject unknown properties, unsupported versions, incomplete envelopes, invalid recipients or
  priority, and missing, extra, or conflicting variant data before a queue mutation.
- Keep priority and creation data in `.handoff.json` filenames for lexical queue ordering, but treat the typed
  document as the metadata authority. Derive CLI payload text from the validated variant.
- Do not ship a legacy reader or migrator. Before creating or processing handoffs, inspect every queue state for
  legacy `.handoff` artifacts and fail the operation before any mutation; this also makes mixed queues fail as a
  unit. Document that existing queues must be drained with the previous version or reset by a normal launch before
  upgrading, while a continued launch preserves only JSON queues.

### Slice 1: Replace the durable handoff representation end to end

This is one atomic slice because changing a writer or reader independently would either break a supported queue
flow or introduce the dual-format behavior this issue removes.

1. Add the versioned handoff envelope, distinct Git and note data types, kind representation, centralized JSON
   serializer options, schema validation, payload projection, `.handoff.json` discovery, and atomic write/update
   operations to `squad.Handoffs`, keeping one top-level type per file.
2. Change `squad handoff commit` and `squad handoff note` to construct validated documents with native recipient and
   priority values and publish them through the shared atomic writer. Preserve command syntax, Git validation,
   filename ordering fields, and outbox ownership.
3. Change Headquarters delivery to deserialize once, validate the complete fan-out before writing any copy, create
   one typed copy per selected recipient, set enqueue metadata, publish every copy atomically and idempotently, and
   archive the sender only after all copies exist. Invalid JSON, schema versions, and variants must leave no
   recipient copies, preserve the source in `failed`, and emit the existing controlled delivery diagnostic.
4. Change task and batch discovery, claiming, display, and completion to use typed documents. Preserve filesystem
   moves as authoritative state transitions, rewrite lifecycle timestamps atomically, retain collision behavior,
   and format priorities as two digits only at filename or CLI presentation boundaries.
5. Add a shared queue-format preflight used by the role CLI and continued Headquarters startup so any legacy or
   mixed queue across `outbox`, `sent`, `failed`, `inbox/new`, `inbox/in_process`, or `inbox/completed` is rejected
   before processing. Diagnostics must identify the unsupported legacy queue and direct the operator to drain it
   with the previous version or reset it with a normal launch.
6. Remove `HandoffHeaders`, delivery-local parsing/rendering, comma-separated persisted recipients, body separator
   handling, and every production line search or mutation after all producers and consumers use the shared model.
7. Update mailbox fixtures and observers to create and inspect JSON independently of production serialization.
   Keep the existing creation, ordering, fan-out, retry, recovery, task, batch, collision, and pump-failure
   scenarios, and add focused black-box coverage for native JSON values, selected recipients, lifecycle timestamp
   preservation, malformed JSON, unsupported versions, invalid variants, and rejection of legacy and mixed queues
   without partial mutation.
8. Update the handoff glossary, architecture, and module inventory with the typed JSON contract, `.handoff.json`
   suffix, derived payload, atomic queue semantics, and finite drain-or-reset upgrade policy. Repeat the
   production-source audit and either confirm the table above or record any newly discovered application-owned
   custom structured format with its disposition.

**Exit criteria:** A handoff created through the real `squad` process remains valid, typed JSON through
Headquarters fan-out, task or batch claim, restart recovery, and completion; every transition remains durable and
atomic; invalid, legacy, or mixed queues fail without losing or partially delivering an artifact; no production
legacy-grammar code remains; and the focused black-box handoff suite passes.

## Slice 1 review (ba0b044129) — changes requested

### Finding 1 — Medium

- **Location:** `docs/manual/glossary.md` (Handoff / Handoff queue); `docs/manual/architecture.md` (State ownership
  and durability; Architectural characteristics item 3); `docs/manual/modules.md` (`squad.Handoffs`).
- **Violated behavior:** Required design and slice 8 require the manual to describe the typed JSON contract, derived
  payload, and the finite drain-or-reset upgrade policy: existing queues must be drained with the previous release
  or discarded by a normal launch; a continued launch preserves only JSON queues and must not start on legacy or
  mixed queues. Architecture currently says a continued launch preserves worktrees and queues with no upgrade
  exception.
- **Root cause:** The commit only swapped `.handoff` for `.handoff.json`, noted a versioned JSON document, and
  mentioned legacy-queue detection. It did not document drain-or-reset, that payload text is derived from variant
  fields rather than persisted, or that `--continue` refuses a legacy or mixed queue.
- **Required outcome:** Update glossary, architecture, and module inventory so an operator can see the JSON
  contract, derived payload, atomic filesystem queue, and drain-or-reset policy without reading source. State
  ownership must not imply that a continued launch preserves a pre-JSON queue.

### Finding 2 — Medium

- **Location:** `src/squad.Specs/Support/Mailboxes/HandoffMailboxObserver.cs` (`Parse` omits timestamps);
  `src/squad.Specs/Features/Delivery.feature`; `src/squad.Specs/Features/Handoffs.feature`;
  `src/squad.Specs/Features/HeadquartersWorkspaceFailures.feature`.
- **Violated behavior:** Acceptance requires lifecycle timestamps to survive each rewrite. Slice 7 requires focused
  black-box coverage for timestamp preservation and for rejecting mixed queues without partial mutation. The suite
  never observes `createdAt` / `enqueuedAt` / `dequeuedAt` / `completedAt` across delivery, claim, or completion. Mixed
  queues are unproven: both new legacy scenarios seed only a `.handoff` artifact, so a sibling `.handoff.json` being
  delivered, claimed, or overwritten would not fail.
- **Root cause:** Observers were switched to JSON property reads for sender, recipients, priority, kind, and
  payload, but timestamps were left unread. Legacy fixtures cover a legacy-only queue, which cannot show that JSON
  artifacts in the same queue stay unprocessed.
- **Required outcome:** Prove through the real `squad` / `squad-hq` processes that timestamps survive fan-out, claim,
  and completion rewrites, and that a mixed queue (legacy `.handoff` plus `.handoff.json` in any scanned state) fails
  as a unit with no JSON artifact created, delivered, claimed, or altered.
