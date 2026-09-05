---
title: too much code in the frontend
priority: 10
---

# Reduce frontend code without moving browser concerns into C#

## Goal

Analyze the frontend and identify meaningful ways to reduce its size:

- keep authoritative application logic in C#;
- avoid duplicating domain rules in Vue;
- prefer maintained npm packages over custom infrastructure when they preserve supported behavior; and
- investigate whether production code can approach an 80% backend / 20% frontend split.

## Analysis result

The frontend is larger than the proposed ratio, but the excess is not caused by domain logic being implemented in
Vue. It is concentrated in a sophisticated virtual transcript and its browser-only scroll, measurement, anchoring,
accessibility, and protocol-reconciliation behavior.

A direct migration of that code to C# would put DOM and transient client state on the wrong side of the protocol.
The only credible material reduction is to replace part of the custom virtualization engine with a maintained
frontend library.

### Baseline

The following physical line counts include production source and exclude tests, generated files, dependencies, and
build output:

| Area | Files | Lines | Share of production code |
|---|---:|---:|---:|
| Backend `.cs` files | 150 | 8,736 | 67.6% |
| Frontend `.ts`, `.vue`, and `.css` files | 37 | 4,196 | 32.4% |
| Total | 187 | 12,932 | 100% |

The 5,366 lines of Playwright tests are not included in the ratio. They characterize behavior that a reduction must
preserve rather than production code that should be reduced.

Reaching 20% frontend code from this baseline would require one of the following:

- delete approximately 2,012 frontend lines without adding replacement backend code; or
- relocate approximately 1,610 lines from frontend to backend one-for-one.

That scale cannot be reached through component cleanup, type deduplication, or ordinary refactoring.

### Concentration

| Frontend area | Lines | Frontend share |
|---|---:|---:|
| `src/squad-ui/src/transcript` | 1,687 | 40.2% |
| `src/squad-ui/src/composables` | 1,594 | 38.0% |
| `src/squad-ui/src/components` | 621 | 14.8% |
| `src/squad-ui/src/protocol` | 157 | 3.7% |
| App bootstrap and styles | 137 | 3.3% |

The transcript directory plus the seven transcript-focused composables contain 2,996 lines, or 71.4% of the
frontend:

- `useTranscriptFeed.ts`
- `useTranscriptHistory.ts`
- `useTranscriptScrollIntent.ts`
- `useTranscriptMutationUpdates.ts`
- `useVirtualWindow.ts`
- `useTranscriptViewportLifecycle.ts`
- `useTranscriptAnnouncements.ts`

The remaining components, interaction drafts, protocol bridge, app shell, and styles are already small. Combining
them into fewer files would reduce module count but not meaningful code or responsibility.

## Ownership assessment

### Application and protocol authority is already in C#

The backend owns the relevant domain behavior:

- `SquadViewModel` owns role state, pending interactions, snapshots, and command admission.
- `RoleTranscriptState` owns transcript ordering, streaming entries, tool correlation, retained content, and
  sequence numbers.
- `TranscriptArchive` owns bounded history, archive paging, truncation, and reconstruction.
- `PhotinoUiCommandHandler` validates and routes UI commands.
- `PhotinoUiDeliveryCoordinator` and `TranscriptAnnouncementJournal` own delivery scheduling, sequence recovery,
  overflow recovery, and announcement recovery.
- `PhotinoTranscriptProtocol` constructs the host payloads.

No substantial session, command, transcript-retention, or interaction rule is authored independently in Vue.

### The large client modules should remain client-side

The following responsibilities depend on live DOM geometry or transient browser state and must remain in Vue:

- variable-height row measurement through `ResizeObserver`;
- bounded DOM window calculation;
- preserving a reading anchor while rows are inserted, replaced, wrapped, or remeasured;
- tail-follow behavior during streaming output;
- distinguishing user, browser, layout, and programmatic scrolling;
- keyboard, focus, selection-autoscroll, resize, and animation-frame ordering;
- accessible live announcements without re-announcing virtualized history; and
- retaining viewport state while paged history or a recovery synchronization arrives.

`useTranscriptFeed.ts`, `useTranscriptHistory.ts`, `TranscriptIndex.ts`, `TranscriptUpdatePlan.ts`, and
`resolveArchivedEntry.ts` can look like application logic because they merge snapshots, updates, pages, and archived
entries. They are a client cache and protocol-reconciliation layer, not an authoritative transcript model. The
backend remains the source of truth; the client preserves locally loaded pages, rejects stale responses, detects a
sequence gap, and asks the backend to synchronize.

Moving this layer to C# would either:

- encode browser viewport/cache state in the backend;
- resend larger transcript snapshots whenever the visible cache changes; or
- leave a second reconciliation layer in Vue and therefore not reduce code.

The projection helpers for timestamps, markers, hidden rows, controls, form drafts, and local formatting are also
presentation concerns.

### Small contract-generation opportunity

`protocol/messages.ts` manually mirrors the JSON payloads built in C#. Typed C# protocol DTOs could become the source
for generated TypeScript declarations when the protocol is extracted into `squad.Ui.Protocol`.

This would reduce drift and remove some handwritten declarations, but it is less than 100 lines and does not
materially affect the ratio. It should be justified by contract safety, not by line-count reduction.

## npm package assessment

Package versions and capabilities were checked on 2026-09-05.

| Candidate | Useful coverage | Important gaps | Estimated net frontend reduction | Decision |
|---|---|---|---:|---|
| [`@tanstack/vue-virtual` 3.13.36](https://www.npmjs.com/package/@tanstack/vue-virtual) | Dynamic row measurement, stable keys, keyed prepend anchoring, end anchoring, conditional append following, and streaming-tail compensation | Sparse transcript/history semantics, load actions, accessibility announcements, and programmatic-versus-user scroll origin remain custom | 450-750 lines | Run a focused spike |
| [`vue-virtual-scroller` 3.0.5](https://www.npmjs.com/package/vue-virtual-scroller) | Dynamic measurement and prepend position preservation | Tail-follow policy remains custom; recycled component instances add focus, local-state, and accessibility risk | 400-700 lines | Second choice only |
| [`virtua` 0.51.0](https://www.npmjs.com/package/virtua) | Dynamic measurement and core viewport compensation | Pre-1.0 API; its own chat example still implements tail and prepend policy in application code | 300-550 lines | Do not prefer over TanStack |
| [`@vueuse/core` `useVirtualList` 14.4.0](https://www.npmjs.com/package/@vueuse/core) | Basic visible-range calculation | No per-row measurement, keyed reflow anchoring, tail following, or scroll-origin support | 0-100 lines at best | Reject |

The estimates are spike targets, not guaranteed savings. No package owns BlaXquad's sparse-history model, archived-entry
loading, recovery protocol, live announcements, or explicit scroll-origin policy.

TanStack Virtual is the strongest candidate because its current chat-oriented API covers the largest custom
mechanical surface. A successful integration could retire all or substantial parts of:

- `PrefixSumIndex.ts`
- `VirtualWindow.ts`
- `RowMeasurements.ts`
- `ViewportGeometryObserver.ts`
- `useVirtualWindow.ts`
- `ReadingAnchor.ts`
- `ScrollController.ts`
- `useTranscriptViewportLifecycle.ts`

Some scroll-intent and transcript-specific adapter code will remain.

## Decision

Do not move the transcript viewport, cache, or protocol-reconciliation logic to the backend merely to improve the
ratio. The current ownership boundary is correct.

Use the 80/20 ratio as a diagnostic, not an acceptance criterion. Even the optimistic 750-line package reduction
would leave the frontend at approximately 28.3% of production code. Reaching 20% while preserving current behavior
would still require moving roughly another 1,010 lines to C# or deleting roughly another 1,262 frontend lines, for
which this analysis found no coherent responsibility.

The meaningful implementation path is a bounded `@tanstack/vue-virtual` spike:

1. Keep the sparse transcript projection, history requests, protocol reconciliation, interaction state, and live
   announcements application-owned.
2. Replace only measurement, virtual-range calculation, keyed anchoring, and tail-follow mechanics supplied by the
   library.
3. Preserve the existing Playwright invariants for bounded DOM size, sparse history, streaming tail following,
   prepend/reflow/recovery anchoring, keyboard/focus/selection scrolling, and exactly-once ordered announcements.
4. Adopt the dependency only if it removes at least 400 net production lines and leaves less custom timing and
   geometry code than the current implementation. Count new adapter code in that comparison.
5. Keep the current implementation if the spike needs parallel custom anchoring or scroll-classification machinery;
   adding a dependency without retiring those responsibilities would increase complexity.

The architectural acceptance criterion should be that C# remains authoritative for domain state, validation,
persistence, and protocol delivery while Vue contains only presentation, browser behavior, transient UI state, and
client cache/protocol reconciliation. The current implementation satisfies that boundary; the remaining opportunity
is implementation reuse, not backend migration.
