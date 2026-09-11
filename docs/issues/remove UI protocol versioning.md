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

## Slice plan

This is one slice: **remove UI envelope versioning end to end**. The C# host, Vue dashboard, stdio acceptance client,
browser harness, fixtures, and documentation describe one contract shipped from one repository. Splitting those
changes would leave an intermediate commit whose packaged peers disagree or whose tests and architecture describe a
contract that no longer exists.

### Slice 1: Remove UI envelope versioning end to end

1. Change the production contract atomically:
   - Make `UiMessageReader.Read` parse an envelope without a protocol-version argument or exact-version branch while
     preserving type validation before command dispatch.
   - Remove the UI protocol constant from `UiProtocolSession` and serialize host envelopes as
     `{ type, payload[, requestId] }`.
   - Remove `PROTOCOL_VERSION` and `Envelope.version` from the Vue protocol model, stop checking host versions, and
     serialize client envelopes as `{ type, ...options }`.
   - Do not reject or otherwise special-case an obsolete extra `version` property; ordinary unknown envelope
     properties remain outside the contract. Do not add a replacement compatibility identifier.
2. Update both black-box protocol clients and their behavioral coverage:
   - Remove the UI version constant and envelope field from `HeadlessUiClient`. Classify stdio protocol output by a
     complete JSON object with a message type rather than by the removed field.
   - In `UiProtocolValidation.feature`, remove only the unsupported-version example and remove `version` from every
     retained raw envelope. Keep the missing/unknown type, missing identifier, invalid payload, malformed JSON,
     no-provider-invocation, recovery, and correlated-error proofs, simplifying step support that existed only for
     the removed case.
   - Remove versioning terminology from the stdio client, scenario facade, feature text, and protocol mapping
     comments.
3. Update the Vue browser boundary and fixtures:
   - Make the shared dashboard harness `ProtocolMessage` and `protocolMessage` helper unversioned.
   - Remove `version` from direct host-message fixtures and expected client messages in the dashboard interaction,
     layout, prompt-history, transcript accessibility, transcript protocol, transcript scrolling, and transcript
     virtualization specifications.
   - Delete the Playwright case that rejects a version-2 host message. Retain the unknown-host-message proof and add
     or retain focused malformed-host-data coverage through the real bridge so removing the version branch does not
     weaken visible diagnostics.
4. Align every directly affected description of the contract:
   - Update `docs/Manual/architecture.md`, `glossary.md`, `modules.md`, and `test-strategy.md`, plus
     `src/squad-ui/README.md`, to call this an internal JSON contract shared by the packaged dashboard and the
     test-only stdio adapter, not a versioned public API for independently deployed clients.
   - Update pending issue `024 establish squad-member-centered application domain model.md` so its migration guidance
     does not prescribe versioning this packaged UI contract; retain migration/versioning requirements only where a
     durable or independently deployed contract actually needs them.
   - Leave the fake-provider control pipe's separate `ControlPipeDuplex.ProtocolVersion`, package metadata versions,
     and unrelated frontend generation/layout version counters unchanged.
5. Validate the complete slice with the affected `UiProtocolValidation` and stdio Gherkin scenarios, the Vue
   type/build check, and the browser suite. Search tracked source, test support, fixtures, and stable documentation
   for leftover UI-envelope constants, `version` fields, unsupported-version branches, and versioned-UI wording.

## Acceptance criteria

- Neither client-to-host nor host-to-client JSON envelopes produced by the application or its test clients contain a
  `version` property.
- No UI-protocol version constant or exact-version rejection branch remains in production code or test support;
  unrelated protocols and non-protocol version counters are unchanged.
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
- Stable architecture, glossary, module, testing, and dashboard documentation no longer describe the UI protocol as
  versioned or imply independently deployed presentation peers.
- No pending issue instructs a future change to bump or negotiate a version for this packaged UI contract.

## Non-goals

- Supporting backend and frontend artifacts from different releases.
- Defining a public compatibility policy for third-party presentation clients.
- Weakening message-type or payload validation.
- Redesigning protocol commands, snapshots, transcripts, or transport framing.
- Adding protocol negotiation or a replacement compatibility identifier.