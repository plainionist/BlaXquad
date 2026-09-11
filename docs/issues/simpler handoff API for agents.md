---
title: simpler handoff API for agents
priority: 50
---

## Problem

The current `squad handoff <draft-file>` interface exposes an intermediate file
format to agents. A typical Git handoff requires an agent to:

1. Run `squad handoff --help` to rediscover the draft schema.
2. Resolve and abbreviate the commit.
3. Create a correctly formatted temporary file.
4. Run `squad handoff <draft-file>`.
5. Repair and retry any formatting errors.

The draft is not a useful durable artifact: the command deletes it after
creating the real outbox handoff. Its schema is also repeated inside the
implementation by the usage text, allowed-field collections, validation, and
serialization. Adding separate format documentation would create another copy
without removing the agent's discovery step. Agents are instructed not to read
arbitrary files on startup, so such documentation would not reliably be in
their context anyway.

The CLI already owns sender resolution, validation, canonical serialization,
and durable publication. It should also own construction of the handoff rather
than requiring the agent to serialize an input file.

## Proposed agent API

Expose the two supported intents as direct subcommands:

```text
squad handoff commit --to reviewer --task "020-protocol-contract"
squad handoff note --to architect,reviewer --message "Need an ownership decision"
```

Use these defaults:

- Infer the sender from the current role worktree, as today.
- Use `HEAD` for a commit handoff.
- Use priority `50` unless `--priority NN` is supplied.
- Accept `--commit <revision>` when an earlier or otherwise explicit commit is
  intentionally handed off.

The command resolves `HEAD` or the supplied Git revision to a commit and stores
the existing canonical ten-character abbreviation. Branch detection is not
needed: `HEAD` in the role worktree is the precise committed state to hand off.

Recipient and intent-specific content remain explicit. The role graph does not
provide a safe recipient default: for example, the architect sends to both the
coder and reviewer at different workflow stages. Likewise, the command should
not infer a task name or note message.

Before handing off the default `HEAD`, reject a worktree with non-ignored
uncommitted changes and explain that the work must be committed first. This
prevents a successful command from silently handing off a stale commit.

`--latest-work` should not be introduced as a separate mode. Defaulting the
`commit` subcommand to `HEAD` provides that behavior with more precise language
and without combining a mode flag with the legacy draft interface.

## Internal design

Parse the command line directly into type-specific requests rather than a
dictionary of draft headers. Keep recipient, priority, task, message, and Git
validation in C#, then pass the validated request to the existing durable
handoff writer. The generated `.handoff` representation and delivery protocol
remain unchanged and become private implementation details of the CLI.

The primary help output should show the two direct command forms and their
options. On validation failure, report the actionable field error and the
relevant one-line invocation rather than printing a file-format specification.

## Agent instructions

Put the two generic command templates in the shared constitution so every role
receives them in its initial context. Role prompts should state only when and to
whom each role hands off. This duplicates a small invocation example in CLI
help, not the handoff file schema, and removes the reason for agents to call
`--help` during normal operation.

Do not add a skill, separate format document, interactive prompt, or native
agent tool for this workflow. A direct, non-interactive CLI call is portable and
already available to every supported agent runtime.

## Migration

1. Add the direct `commit` and `note` forms and cover them through the existing
   black-box handoff feature.
2. Update the shared constitution and role workflows to use the direct forms.
3. Keep `squad handoff <draft-file>` temporarily only if compatibility with
   already-running or external agents is required; do not present it as the
   primary help path.
4. Remove the draft parser and compatibility form once all bundled callers use
   the direct API.

## Acceptance criteria

- A role can queue its `HEAD` with one `squad handoff commit` invocation and no
  temporary input file.
- A commit handoff defaults to priority `50` and supports explicit commit and
  priority overrides.
- A role can queue a note with one `squad handoff note` invocation, including
  multiple comma-separated recipients.
- Existing sender, recipient, field-length, priority, and Git commit validation
  remains enforced.
- A dirty worktree cannot accidentally produce a default-`HEAD` handoff.
- The durable outbox representation and downstream delivery behavior do not
  change.
- Bundled agent instructions contain enough command syntax for normal handoffs
  without first invoking `squad handoff --help`.