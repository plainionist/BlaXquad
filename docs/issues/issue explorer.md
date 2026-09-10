---
title: issue explorer
priority: 9999
---

# Issue explorer

## Goal

Add a compact issue explorer above the agent panels so an operator can inspect repository issues, copy an issue path,
or prepare the first configured role to process an issue without sending the prompt automatically.

## User experience

- Add a toolbar above the agent panels with an `Issues` menu trigger aligned to the left.
- Opening the menu lists the Markdown files directly inside `docs/issues`.
- Order issues by numeric frontmatter priority, lowest number first.
- Show the frontmatter title as the entry label. If no valid title is present, show the filename.
- Place issues without a valid priority after prioritized issues and order them alphabetically by filename.
- Hovering an entry selects it. Keyboard focus must provide the same behavior.
- Show a flyout for the selected issue containing its frontmatter and the first five non-empty lines after the
  frontmatter.
- Close the menu with Escape, an outside click, or the menu trigger.
- If the directory is missing or contains no issues, show a disabled `<no issues>` entry.

Each issue entry has two icon actions:

- **Copy** copies the issue path relative to the workspace root, using `/` separators, to the clipboard. For example:
  `docs/issues/issue explorer.md`.
- **Play** puts `process this issue: '<relative path>'` into the prompt draft of the first configured role and focuses
  that role's prompt composer. It must not emit `prompt.send`; the user still submits the prompt with Enter or Send.

If no role is available, keep Play disabled while leaving Copy available.

## Architectural analysis

The Play behavior is frontend-only because prompt drafts are transient Vue state. Issue discovery is not
frontend-only: the browser must not read arbitrary workspace files, so headquarters must discover and parse the
fixed `docs/issues` directory and return a typed read model through the UI protocol.

Do not add issue data to `state.snapshot`. Agent-state snapshots are published frequently, while issue documents
have an independent lifecycle. Instead, add a dedicated `issues.list` request and response. The dashboard requests
the catalog whenever the menu opens, which reflects edits made during a running session without introducing a file
watcher or repeatedly reading files on agent-state updates.

The backend owns these rules:

- directory and file discovery;
- YAML frontmatter parsing;
- title and priority fallback behavior;
- deterministic ordering;
- body-preview extraction; and
- workspace-relative path normalization.

The frontend owns menu state, selection, flyout placement, clipboard interaction, prompt-draft updates, focus, and
responsive presentation. Markdown and frontmatter previews are displayed as plain text, not injected HTML.

## Protocol shape

Add an `issues.list` command with no client-supplied path. Its response contains an ordered collection conceptually
equivalent to:

```text
path: string
title: string
priority: number | null
frontmatter: string
previewLines: string[]
```

The fixed server-side directory prevents the command from becoming a general workspace-file API. A missing
directory is a successful empty result. A malformed or partial frontmatter block must not make the whole catalog
unavailable: retain the issue and apply field-level fallbacks. Surface genuine directory or file I/O failures as
protocol errors rather than reporting a misleading empty catalog.

This is a protocol extension, so increment the protocol version in C# and TypeScript together.

## High-level implementation plan

1. Define the issue descriptor and catalog operation at the UI-facing boundary.
2. Implement a small read-only C# catalog for `<workspace>/docs/issues` using a YAML parser rather than ad hoc
  frontmatter matching. Enumerate top-level `.md` files only, normalize returned paths, apply fallbacks, and sort
  the final descriptors.
3. Route `issues.list` through `UiProtocolSession` and wire the catalog from the headquarters composition root into
  both Photino and stdio hosts. Keep issue filesystem concerns out of `SquadViewModel`.
4. Extend the TypeScript protocol types and bridge with the issue-list response.
5. Add an issue-explorer component and session state to the Vue dashboard. Request fresh data on open and implement
  pointer, keyboard, empty, loading, and error states.
6. Implement Copy with the browser clipboard API and accessible confirmation.
7. Implement Play by updating the first role's existing draft state and focusing its composer. Make configured role
  order an explicit, tested invariant rather than relying on incidental dictionary enumeration.
8. Integrate the toolbar into the current responsive layout and ensure the menu and flyout remain within the
  viewport on desktop and mobile.

## Test strategy

Add black-box Gherkin coverage through the real stdio UI protocol for:

- priority ordering and deterministic tie-breaking;
- title and priority fallbacks;
- frontmatter and body-preview extraction;
- a missing or empty `docs/issues` directory;
- normalized workspace-relative paths; and
- refreshing the result after issue files change during a running headquarters session.

Add focused Playwright coverage for:

- opening and closing the menu;
- the disabled `<no issues>` entry;
- mouse and keyboard selection with the corresponding flyout;
- copying the exact relative path;
- putting the exact prompt into the first configured role's draft;
- preserving other roles' drafts and emitting no protocol command until the user submits;
- disabling Play when no role is available; and
- desktop and narrow-screen layout without clipping or overlap.

## Decision for review

Playing an issue while the first role already has a non-empty draft can destroy user input. The recommended behavior
is to ask for confirmation before replacing that draft. An alternative is to append the issue prompt on a new line,
but that makes the resulting instruction less predictable.

