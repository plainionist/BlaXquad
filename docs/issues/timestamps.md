---
title: timestamps
priority: 1
---

in the transcript show a timestamp before every message

## Architecture

`TranscriptEntry.OccurredAt` is already the authoritative event time and is
carried unchanged through synchronization, paging, archived-entry recovery,
and live updates. This feature is therefore presentation-only:

- keep timestamp ownership and transport in C# unchanged;
- format `occurredAt` in the Vue transcript projection as browser-local
  24-hour time with seconds (`HH:mm:ss`);
- render the label in a semantic `<time>` element whose `datetime` value is
  the original protocol value; and
- place the timestamp before the existing marker, prefix, and content while
  preserving the current marker and source styling.

One timestamp represents one transcript entry, including multi-line and
streamed entries. Content replacement or append-content updates must not add
another timestamp. The transient `Thinking ...` row is not a transcript
message and has no authoritative `occurredAt`, so it remains untimestamped.
Live-region announcements remain content-only to avoid repeatedly announcing
timestamps during streaming.

## Implementation slices

### Slice 1 (in progress): Render transcript entry timestamps

1. Extend the transcript presentation projection with a formatted local-time
   label derived from `occurredAt`. Keep the original value available for the
   semantic `datetime` attribute; do not change protocol types or create
   timestamp state in Vue.
2. Update `VirtualTranscript.vue` to render exactly one timestamp before each
   projected transcript entry.
3. Add transcript timestamp styling in `style.css` with a stable, non-shrinking
   width and subdued color so message content remains aligned and readable.
4. Add focused Playwright coverage for synchronized history and live updates,
   including source markers/prefixes, semantic `datetime`, local
   `HH:mm:ss` formatting, and the absence of duplicate timestamps after
   streamed content updates.

**Acceptance criteria**

- Every rendered transcript entry shows its browser-local `HH:mm:ss` before
  its marker, source prefix, and content.
- Each visible timestamp is a `<time>` element whose `datetime` attribute
  exactly preserves the entry's `occurredAt`.
- Historical, paged, archived/recovered, and newly appended entries use the
  same rendering path without backend or protocol changes.
- Append-content and replace updates retain exactly one timestamp for the
  affected entry.
- Multi-line entries show one timestamp at the beginning of the entry rather
  than one timestamp per visual line.
- Existing transcript virtualization, scrolling, source markers, prefixes,
  and content-only live announcements remain unchanged.
- The synthetic `Thinking ...` row remains untimestamped.

**Coder handoff**

Implement Slice 1 only. The expected change surface is
`src/squad-ui/src/transcript/transcriptProjection.ts`,
`src/squad-ui/src/components/transcript/VirtualTranscript.vue`,
`src/squad-ui/src/style.css`, and focused tests under
`src/squad-ui/tests/`. Do not change the C# transcript model or protocol.
