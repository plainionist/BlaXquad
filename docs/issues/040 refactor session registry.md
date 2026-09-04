---
title: Refactor SessionRegistry internal responsibilities
priority: 40
---

# Refactor SessionRegistry internal responsibilities

`SessionRegistry` correctly acts as the authoritative lifecycle and command-admission owner, but it also implements all
of the mechanics for session lookup, command lease tracking/draining, failed-abort barriers, capability projection, and
transition serialization in one class.

## Refactoring direction

Preserve `SessionRegistry` as the single public lifecycle authority and atomic lock boundary. Extract cohesive internal
state helpers, preferably:

1. A session catalog for generation-scoped role/session identity, live-session lookup, and failed-abort barriers.
2. A command lease tracker for admitted command registration, cancellation, draining, release, and transfer to backend
   ownership.

`SessionRegistry` must still make phase checks, session selection, and command admission atomically. The helpers must not
expose independent lifecycle APIs, acquire unrelated locks, or become pass-through services. Capability snapshots must
continue to derive from the same authoritative state used for admission.

## Acceptance criteria

- There remains exactly one lifecycle phase, generation counter, transition exclusion mechanism, and admission owner.
- Session identity/abort-barrier mechanics and command tracking/draining mechanics have cohesive internal owners.
- Beginning relaunch or stop still closes admission and retires commands atomically.
- Lease currency, capability, failed-abort, drain, transfer, and transition behavior is unchanged.
- Existing black-box Gherkin lifecycle, prompt, abort, relaunch, and shutdown scenarios remain green.

## Why priority 40

The registry is architecturally important, but most of its code serves one lifecycle aggregate. This cleanup improves
maintainability without justifying the disruption of a higher-priority redesign.