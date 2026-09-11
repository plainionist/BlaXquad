---
title: Remove UI protocol versioning
priority: 2
---

# Remove UI protocol versioning

## Goal

Remove the protocol-version field and exact-version checks from the Headquarters-to-dashboard JSON contract. The
backend and frontend are built, tested, packaged, and shipped together as one product artifact, so independently
evolving protocol peers are not a supported deployment model.

Versioning currently duplicates that release invariant while coupling every protocol change to synchronized C#,
TypeScript, harness, and fixture updates. It detects a mismatch but provides no negotiation or compatibility because
each side accepts exactly one version.

Complete this issue before extending the UI protocol for Git history so that feature does not introduce another
version bump and another round of fixture churn.

## Architecture decisions

- Treat the packaged dashboard and Headquarters as one release unit with one internal JSON contract.
- Remove `version` from client and host envelopes, including production serialization, TypeScript types, browser
  harness messages, stdio-hosted acceptance messages, and test fixtures.
- Remove protocol-version constants and exact-version rejection branches from C#, Vue, and shared test support.
- Preserve strict envelope validation for message type and any command-specific identifiers and payloads.
- Continue reporting malformed JSON, missing or invalid message types, unknown commands, invalid payloads, and command
  failures as visible `protocol.error` messages. Removing versioning must not weaken these diagnostics.
- Keep request correlation, transcript ordering, synchronization, recovery, and delivery semantics unchanged.
- Do not introduce replacement compatibility machinery such as version ranges, capability negotiation, schema IDs, or
  frontend build hashes. Add such a mechanism only if independently deployed UI clients become a supported product
  requirement.
- The stdio hosting adapter remains a transport for the same internal contract and is built from the same source; it
  does not define a separately versioned public API.

## Implementation plan

1. Characterize the observable validation behavior that remains supported through the existing black-box Gherkin
   UI-protocol scenarios.
2. Remove the protocol-version argument and check from C# envelope parsing, remove the C# version constant, and stop
   serializing `version` on host messages.
3. Remove the TypeScript version constant and envelope field, stop checking host versions, and stop adding a version
   to client messages.
4. Update the browser harness, stdio protocol support, acceptance data, and Playwright fixtures to use unversioned
   envelopes. Remove the unsupported-version scenario rather than replacing it with a test whose only purpose is
   proving that removed behavior is unavailable.
5. Retain or extend high-value black-box coverage for malformed envelopes, missing types, unknown message types,
   invalid payloads, correlated failures, and valid bidirectional messages.
6. Update `docs/manual/architecture.md` and `docs/manual/modules.md` to describe the internal JSON UI protocol without
   claiming it is versioned. Remove version-increment instructions from pending issues that extend this protocol.

## Acceptance criteria

- Neither client-to-host nor host-to-client JSON envelopes contain a `version` property.
- No production or test-support protocol-version constant remains.
- Adding a UI message or payload field no longer requires coordinated version-number changes or unrelated fixture
  edits.
- Headquarters accepts a valid typed command without a version field and rejects malformed envelopes, unknown
  commands, and invalid command payloads with the existing visible protocol-error behavior.
- Vue accepts valid host messages without a version field and still reports malformed data and unknown host message
  types visibly.
- Request correlation and transcript ordering, synchronization, recovery, and state-preservation behavior remain
  unchanged.
- The packaged Photino dashboard and the stdio-hosted acceptance client both use the same unversioned contract.
- The affected black-box Gherkin scenarios and focused Playwright specifications pass.
- Stable architecture and module documentation no longer describe the UI protocol as versioned.

## Non-goals

- Supporting backend and frontend artifacts from different releases.
- Defining a public compatibility policy for third-party presentation clients.
- Weakening message-type or payload validation.
- Redesigning protocol commands, snapshots, transcripts, or transport framing.
- Adding protocol negotiation or a replacement compatibility identifier.