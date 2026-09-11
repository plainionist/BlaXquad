---
title: remove unnecessary custom exceptions
priority: 1
---

custom exceptions must have a value - if those are not caught and handled explicitly they just create overhead and should be removed

## Analysis

The repository defines seven custom exception types. Six have concrete value because a caller catches them to change
control flow, process exit behavior, or failure presentation:

- `CliExitException` carries an exit code and is translated at executable boundaries.
- `WorkspacePreparationException`, `AgentBackendTerminalFailureException`, and
  `HandoffPumpTerminalFailureException` let `squad-hq` report distinct failure categories.
- `SquadConfigurationException` separates configuration failures at the configuration/workspace boundary.
- `ShutdownBeforeReadyException` distinguishes an expected early shutdown from a startup failure.

`HandoffFormatException` is the only custom exception that no caller handles as a distinct failure. Its one typed catch
is inside `HandoffJson.Read` and only adds source-path context before the delivery boundary handles every per-file
failure uniformly. The standard `InvalidDataException` already models malformed or semantically invalid durable
handoff data, including validation of an in-memory document before it is written.

## Implementation plan

### Slice 1: Use the standard invalid-data failure for handoff documents

Replace `HandoffFormatException` with `InvalidDataException` throughout `squad.Handoffs`, preserving the existing
diagnostic messages, source-path context, and inner exceptions for malformed JSON and invalid document structure.
Update the affected API documentation and delete the custom exception type. Do not change handoff validation,
serialization, delivery, or failure-archiving behavior, and retain the six custom exceptions whose callers explicitly
distinguish them.

Acceptance criteria:

- `HandoffFormatException` no longer exists or has any references.
- Invalid in-memory handoff documents and unsupported handoff kinds fail with `InvalidDataException`.
- Reading malformed JSON still reports the source path and preserves the `JsonException` as its inner exception.
- Reading a structurally invalid handoff still reports the source path and preserves the validation failure as its
  inner exception.
- The existing `Delivery.feature` malformed-JSON and mismatched-kind scenarios still archive the sender artifact as
  failed before creating any recipient copy or wake-up.
- Valid handoff delivery and lifecycle behavior remain unchanged.
