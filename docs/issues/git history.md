---
title: git history
priority: 1
---

Add a Git history action next to Issues in the dashboard. It launches the configured history command in the
workspace connected to Headquarters. When the configured executable cannot be found, the action remains visible
but disabled. Present both actions as a bordered icon toolbar with tooltips.

The issue icon may take visual direction from
[plainionist/md-issue-explorer](https://github.com/plainionist/md-issue-explorer); the history icon should communicate
branching/history.

## Architecture decisions

- Add an optional top-level `gitHistoryCommand` array to `blaxquad/squad.json`. The first item is the executable and
  every remaining item is one exact argument. Configure this repository with:

  ```json
  "gitHistoryCommand": [
    "TortoiseGitProc.exe",
    "/command:log"
  ]
  ```

  An array avoids shell parsing and preserves argument boundaries. When present, it must contain a non-blank
  executable and only non-blank items; malformed values are configuration errors. When omitted, Git history is
  unavailable rather than a launch failure.
- Resolve the executable once during launch using the Headquarters process's `PATH` and Windows `PATHEXT`, while
  continuing to support an explicitly qualified executable path. A configured executable that does not resolve is
  an expected unavailable capability, not a configuration or startup error.
- Keep command validation and process launch in C#. Vue receives only availability and emits an open request; it
  never receives, parses, or executes the configured command.
- Launch the resolved executable directly, pass the configured arguments without interpolation, and set
  `ProjectLayout.WorkingDir` as its working directory. Do not invoke a shell, capture output, or wait for the
  independent GUI process to exit.
- Carry the parsed command through the existing immutable prepared-launch result. Configure one narrow,
  workspace-scoped tool service before the window starts, and share that service with the UI protocol through the
  hosting context. This preserves the single configuration read and keeps process I/O out of the application view
  model and Vue.
- Extend the versioned UI protocol (and increment its version) with:
  - host message `workspace-tools.snapshot` carrying `gitHistoryAvailable`;
  - client command `git-history.open`.

  Publish tool availability as part of the `ui.ready` handshake. The client defaults to unavailable until that
  message arrives. A forged or stale open command while unavailable, and a process-start failure after availability
  was published, must produce a visible `protocol.error` rather than silently succeeding.
- Keep the toolbar assets local and dependency-free. Draw compact `currentColor` SVGs for an issue list and a
  branching history symbol, using the linked project only as visual direction rather than copying its asset. The
  browser must not fetch remote assets at runtime.

## Implementation slices

### Slice 1 - Launch configured Git history

**Status:** in progress

Deliver the complete functional path with the existing text-based Issues control and a text-based Git history
button beside it. Visual toolbar framing and icon conversion belong only to Slice 2.

1. Extend the strict configuration document, validated configuration model, workspace preparation context, and
   immutable prepared-launch result with `gitHistoryCommand`. Update this repository's `blaxquad/squad.json`.
2. Extend executable discovery so the launch path can retain the resolved executable, not merely a Boolean result.
   Add the narrow workspace-tool service described above and wire the same instance through Headquarters startup,
   runtime initialization, `HostingContext`, both hosting adapters, and `UiProtocolSession`.
3. Publish `workspace-tools.snapshot` during `ui.ready`, handle `git-history.open` in the protocol command handler,
   and increment the matching C# protocol, TypeScript protocol, and browser harness versions.
4. Add client bridge/session state for the capability and command. Render Git history directly beside Issues,
   disabled unless the host reports it available, without changing issue catalog loading, selection, copy, or play
   behavior.
5. Add black-box Gherkin coverage through the published stdio-hosted Headquarters process. Arrange commands with
   platform test support rather than production test hooks, and prove:
   - an executable found through `PATH` is reported available;
   - requesting Git history starts the configured executable with exact arguments and the project root as its
     current directory;
   - an omitted or unresolvable command is reported unavailable without preventing startup;
   - requesting an unavailable command returns a protocol error;
   - a malformed configured command terminates launch with the normal configuration diagnostic.
6. Add focused Playwright coverage proving the button starts disabled, follows host-published availability, emits
   exactly one `git-history.open` command only when enabled, and leaves all Issues behavior intact.
7. Update the directly affected configuration, UI-contract, process, and module descriptions in `docs/Manual`.

**Acceptance criteria**

- The checked-in TortoiseGit command is data in `blaxquad/squad.json`; no executable name or argument is hard-coded
  in product code.
- On a machine where `TortoiseGitProc.exe` resolves, activating Git history opens it in the Headquarters workspace
  with `/command:log`; otherwise Headquarters still starts and the dashboard action is disabled.
- Neither command text nor executable paths cross into Vue, and no shell interprets configured arguments.
- Backend command failures are observable protocol errors, and all existing issue-explorer behavior remains
  supported.
- The relevant Gherkin acceptance scenarios and focused Playwright specifications pass.

### Slice 2 - Present the workspace icon toolbar

**Status:** pending Slice 1 acceptance

Make the two working actions a cohesive presentation component without changing their protocol behavior.

1. Introduce a workspace-toolbar presentation owner that contains the existing issue explorer trigger and the Git
   history action. Move relative overlay anchoring into the issue explorer so its menu and preview continue to
   float correctly inside the new container.
2. Style the container as a compact bordered toolbar panel with consistent spacing, background, focus treatment,
   hover treatment, and a clearly dimmed disabled state.
3. Replace both visible text labels with locally authored, `currentColor` SVG icons: a document/list icon for Issues
   and a branching/history icon for Git history. Keep the SVGs decorative and dependency-free.
4. Preserve accessible button names with `aria-label="Issues"` and `aria-label="Git history"`, and add matching
   `title` attributes for native tooltips.
5. Extend focused Playwright coverage to verify the toolbar grouping, icon-only controls, labels/tooltips, disabled
   presentation, and desktop plus 390-pixel layout containment for the toolbar, issue menu, preview, and role
   panels.

**Acceptance criteria**

- Issues and Git history appear as adjacent icon buttons inside one visibly bordered toolbar panel.
- Hovering either action exposes its name, keyboard focus remains visible, and assistive technology sees the same
  names even though visible text is removed.
- Git history retains Slice 1's availability and launch behavior.
- Opening and using Issues remains behaviorally unchanged, and the toolbar and overlays remain contained at desktop
  and narrow viewport sizes.
- The focused Playwright specifications pass.
