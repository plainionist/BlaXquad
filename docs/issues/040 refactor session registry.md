---
title: Refactor SessionRegistry internal responsibilities
priority: 40
---

# Refactor SessionRegistry internal responsibilities

The restart design requires one authoritative lifecycle and command-admission owner without concentrating all session
lookup, command lease tracking/draining, failed-abort barriers, capability projection, and transition mechanics in one
class.

## Disposition

**Status: deferred to Slice 3 of `restart button.md`; no independent implementation slice**

The current `SessionRegistry` is still the small pre-relaunch type: it owns only role/session registration, active
session lookup, and a shutdown admission flag. It does not yet contain lifecycle phases, generations, command leases,
draining, transfer, capability projection, transition serialization, or failed-abort barriers, so there is nothing to
extract safely in the current code.

The restart issue is authoritative for introducing the lifecycle aggregate. Its preparation section requires this
internal split to be applied from the start in its Slice 3, after backend runtime ownership exists, rather than first
creating a large registry and refactoring it afterward. At that point:

- create the session catalog and command lease tracker as internal collaborators in the same change that introduces
  authoritative lifecycle state;
- keep phase checks, generation selection, transition exclusion, command admission, and capability publication in one
  atomic lifecycle owner; and
- do not expose the current unleased `GetActive` API from the new lifecycle boundary.

This issue is therefore a design constraint on `restart button.md`, not a prerequisite or coder handoff for
`060 split squad core.md`.

## Refactoring direction

When the restart lifecycle aggregate is introduced, preserve it as the single public lifecycle authority and atomic
lock boundary. Give cohesive internal state to:

1. A session catalog for generation-scoped role/session identity, live-session lookup, and failed-abort barriers.
2. A command lease tracker for admitted command registration, cancellation, draining, release, and transfer to backend
   ownership.

The lifecycle aggregate must make phase checks, session selection, and command admission atomically. The helpers must
not expose independent lifecycle APIs, acquire unrelated locks, or become pass-through services. Capability snapshots
must derive from the same authoritative state used for admission.

## Acceptance criteria

- There remains exactly one lifecycle phase, generation counter, transition exclusion mechanism, and admission owner.
- Session identity/abort-barrier mechanics and command tracking/draining mechanics have cohesive internal owners.
- Beginning relaunch or stop still closes admission and retires commands atomically.
- Lease currency, capability, failed-abort, drain, transfer, and transition behavior is unchanged.
- Existing black-box Gherkin lifecycle, prompt, abort, relaunch, and shutdown scenarios remain green.

## Why priority 40

The registry is architecturally important, but most of its code serves one lifecycle aggregate. This cleanup improves
maintainability without justifying the disruption of a higher-priority redesign.