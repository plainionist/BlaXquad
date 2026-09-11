---
title: issue explorer
priority: 9999
---

# Issue explorer

## Goal

Add a compact issue explorer above the agent panels so an operator can inspect repository issues, copy an issue path,
or prepare the configured leader role to process an issue without sending the prompt automatically.

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
- **Play** puts `process this issue: '<relative path>'` into the configured leader's prompt draft and focuses that
  role's prompt composer. It must not emit `prompt.send`; the user still submits the prompt with Enter or Send.

If the configured leader is not available in the current UI state, keep Play disabled while leaving Copy available.

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

Add an `issues.list` command with no client-supplied path. The client assigns the request an envelope `requestId`;
the matching `issues.list` response, or a `protocol.error` caused by that request, echoes that ID so the explorer can
finish its own loading state without treating unrelated protocol failures as catalog failures. The successful payload
is:

```text
issues: [
  {
    path: string
    title: string
    priority: number | null
    frontmatter: string
    previewLines: string[]
  }
]
```

The fixed server-side directory prevents the command from becoming a general workspace-file API. A missing
directory is a successful empty result. A malformed or partial frontmatter block must not make the whole catalog
unavailable: retain the issue and apply field-level fallbacks. Surface genuine directory or file I/O failures as
protocol errors rather than reporting a misleading empty catalog.

This is a protocol extension, so increment the protocol version in C# and TypeScript together.

## Resolved design decisions

- Play replaces the configured leader's current draft immediately. It does not append or ask for confirmation.
- `blaxquad/squad.json` has a required top-level `leader` field whose value must exactly match one configured role
  name. There is no positional or first-role fallback.
- The catalog is an independent, read-only filesystem concern. Put its transport-neutral descriptor and narrow
  catalog contract in `squad.Ui.Abstractions`, implement it in a cohesive `squad.Issues` module, and inject it from
  the `squad-hq` composition root. `SquadViewModel` remains unchanged by issue discovery.
- Recognize frontmatter only when the first line is `---`; the next `---` line closes it. Return the source block,
  including delimiters, with line endings normalized to `\n`. With an opening delimiter but no closing delimiter,
  retain the remaining text as partial frontmatter and return no body preview.
- Parse the text inside the delimiters with YamlDotNet. A valid title is a non-empty scalar. A valid priority is an
  invariant-culture integer. Resolve title and priority independently so one invalid field does not discard the
  other; malformed YAML falls back to the filename and a null priority.
- Preview the first five non-blank body lines after frontmatter, preserving each returned line's text. A document
  without frontmatter uses its first five non-blank lines.
- Enumerate only regular, top-level files whose extension is `.md` case-insensitively. Do not recurse or follow file
  or directory links outside the fixed catalog directory.
- Sort prioritized issues by ascending priority, then filename using ordinal-ignore-case and ordinal tie-breakers.
  Sort unprioritized issues after them by the same filename comparison.
- Treat an absent directory and a present empty directory as successful empty catalogs. A non-directory occupying
  `docs/issues`, enumeration failures, and file-read failures are protocol errors. YAML content errors are descriptor
  fallbacks, not protocol errors.
- Use the order in the configured `roles` array as the authoritative role order. Preserve it explicitly in snapshots
  rather than relying on `Dictionary` enumeration, but use `leader`, never array position, to choose the issue target.

## Delivery plan

### Slice 1 - List issues through the UI protocol [done]

**Outcome:** A UI client can request a fresh, ordered issue catalog through the real headquarters protocol without
gaining a general workspace-file API.

Implementation:

1. Add one-file-per-type `IssueDescriptor` and `IIssueCatalog` contracts to `squad.Ui.Abstractions`.
2. Add `squad.Issues` to `squad.slnx` and the module inventory. Implement `WorkspaceIssueCatalog` against the fixed
   `<workspace>/docs/issues` location with YamlDotNet and the parsing, fallback, preview, path, link, and ordering
   rules above.
3. Construct the catalog in `squad-hq` and pass the same dependency through both `PhotinoWindowHost` and
   `StdioWindowHost` to `UiProtocolSession`. Add `issues.list` routing without adding issue state to
   `state.snapshot`.
4. Echo the request ID on the `issues.list` response and on command failures. Preserve existing uncorrelated
   protocol-error behavior for envelope failures that have no usable request ID.
5. Add the TypeScript descriptor/payload types and bridge callback for `issues.list`. Increment the protocol version
   from 3 to 4 in C#, TypeScript, backend test support, Playwright support, and all protocol fixtures in this slice.
6. Add a focused Gherkin feature through the published `squad-hq --ui stdio` boundary. Extend
   `ScenarioWorkspace`, `BackendScenario`, and `HeadlessUiClient` only with semantic issue-file arrangement,
   catalog-request, and catalog-observation operations.

Acceptance criteria:

- A catalog response contains only top-level Markdown issues and returns workspace-relative `/`-separated paths.
- One response proves priority ordering, deterministic tie-breaking, title and priority field-level fallbacks,
  normalized raw frontmatter, and exactly five non-blank preview lines.
- Missing and empty issue directories return `issues: []`.
- Re-requesting after files change during the same headquarters session returns the changed catalog.
- A real filesystem failure produces a correlated `protocol.error`, while malformed or partial YAML retains the
  affected issue with the documented fallbacks.
- Existing snapshot, transcript, Photino, and stdio behavior remains compatible with protocol version 4.

**Status: complete (793f60a13e).** `issues.list` is served from `squad.Issues` through both window hosts at protocol
version 4, with request-id correlation on success and command failure. `IssueCatalogProtocol.feature` proves empty
and missing directories, ordering and field fallbacks, exclusivity of top-level case-insensitive Markdown paths,
closed/unclosed/absent frontmatter previews, in-session refresh, and a correlated filesystem `protocol.error`.
`docs/manual/modules.md` inventories the new module.

### Slice 2 - Browse and preview issues [done]

**Outcome:** An operator can open the Issues menu and inspect the current catalog with equivalent pointer and keyboard
behavior.

Implementation:

1. Add a focused issue-catalog composable around the shared bridge rather than mixing catalog lifecycle into
   transcript or authoritative role state. Generate a new request ID and request `issues.list` on every closed-to-open
   transition; track loading, the latest matching response, and a matching correlated error.
2. Add an `IssueExplorer` component and a left-aligned toolbar above the role grid and empty-role view. Render loading,
   error, ordered issue, and disabled `<no issues>` states without caching issue data in snapshots.
3. Select an entry on pointer hover or focus within that entry. Show raw frontmatter and preview lines as text only;
   do not use HTML injection.
4. Toggle with the Issues trigger and close on Escape or an outside click, restoring focus to the trigger after an
   Escape close. Keep menu/flyout focus order and accessible names explicit.
5. Fit the toolbar, menu, and flyout inside both the current desktop grid and narrow stacked layout without obscuring
   or horizontally expanding the role panels.
6. Add focused Playwright coverage in a dedicated issue-explorer spec and shared harness support for correlated
   catalog responses and errors.

Acceptance criteria:

- Every opening sends one fresh `issues.list` request, and only its matching response/error completes that load.
- Hovering an issue and focusing its row or action controls select the same issue and show the same plain-text flyout.
- The trigger, Escape, and an outside click close the menu; unrelated inside interaction does not.
- Missing/empty results show a disabled `<no issues>` row, and catalog errors are visible and retry on the next open.
- Desktop and 390-pixel-wide Playwright viewports contain the toolbar, menu, flyout, and role panels without clipping
  or overlap.

**Status: complete (8501560196).** `IssueExplorer` and `useIssueCatalog` request a fresh `issues.list` on each open
and complete the load only from the matching response or correlated error. Playwright covers hover/focus plain-text
preview, trigger/Escape/outside-click close, empty and error retry, and desktop/390 layout: workspace is `100vh`,
the role grid consumes the remaining desktop space, stacked panel min-heights subtract the toolbar, and the layout
test observes full explorer containment plus a 390px role-panel box. Shrinking the transcript viewport makes
`updates virtual row geometry when wrapped content changes width` fail deterministically; that brittleness is
outside this slice.

### Slice 3 - Copy an issue path [done]

**Outcome:** An operator can copy an issue's exact workspace-relative path even when no role is configured.

Implementation:

1. Add a clearly named icon button per issue that writes the descriptor's existing normalized `path` to the browser
   clipboard without rebuilding or platform-normalizing it in Vue.
2. Announce copy success through an `aria-live` status and surface clipboard rejection as an explicit, recoverable
   error; do not report success when the browser rejects the write.
3. Add Playwright coverage with a clipboard spy for the exact path, accessible action name, success announcement,
   failure state, and availability with an empty roles snapshot.

Acceptance criteria:

- Copy writes exactly `docs/issues/issue explorer.md` for the example issue.
- Copy remains enabled when no role exists and never sends a UI protocol command.
- Assistive technology receives accurate success or failure feedback.

**Status: complete (ac4845c3f6).** Each issue row has a Copy control that writes the descriptor `path` through
`navigator.clipboard.writeText` with no Vue path rewriting. Playwright spies the clipboard for the exact path,
announces success on an `aria-live` status, shows a recoverable error without a success claim when the write is
rejected, and keeps Copy enabled with an empty roles snapshot without emitting a UI protocol command.

### Slice 4 - Prepare the first role's prompt [done]

**Outcome:** Play replaces and focuses the first configured role's draft without submitting it or changing any other
role's draft.

Implementation:

1. Make `SquadViewModel` preserve initialization/configuration order explicitly for role snapshots and transcript
   projections. Add black-box multi-role Gherkin coverage that the `state.snapshot.roles` order matches
   `blaxquad/squad.json`.
2. Derive the Play target from the first role in that ordered snapshot. Set its draft to
   `process this issue: '<relative path>'`, replacing any existing target draft immediately.
3. Focus the target role's existing prompt textarea after Vue applies the draft. Keep this as presentation behavior;
   do not add a backend command or duplicate draft state.
4. Disable Play when the ordered role list is empty while leaving Copy enabled.
5. Add Playwright coverage for exact replacement text, configured-order targeting, focus, preservation of other
   drafts, disabled behavior with no roles, and no `prompt.send` until the operator explicitly submits.

Acceptance criteria:

- With roles configured as `reviewer, coder`, Play targets `reviewer` even if other runtime activity arrives first.
- A non-empty target draft is replaced exactly; every non-target draft is unchanged.
- The target textarea has focus and no protocol envelope is emitted by Play.
- Enter or Send after Play uses the existing prompt path and emits the normal single `prompt.send`.

**Status: complete (476254f8ab).** Play stages `process this issue: '<path>'` on the first snapshot role, focuses that
composer, and does not emit `prompt.send`. `SquadViewModel` enumerates `myRoleOrder` for snapshots and transcript
projections; `RoleOrder.feature` proves configured order through the real UI protocol. Playwright covers exact
replacement and focus with other drafts preserved, first-configured targeting despite later reviewer activity, no
envelope until Enter, and Play disabled / Copy enabled with no roles.

**Transition:** Slice 4 is accepted. Slice 5 replaces its positional target; the overwrite, focus, draft-isolation,
disabled-state, and no-auto-send behavior remain.

### Slice 5 - Target the configured leader (in progress)

**Outcome:** Play targets the explicitly configured squad leader regardless of role ordering.

Configuration shape:

```json
{
  "leader": "architect",
  "roles": [
    { "name": "architect", "worktree": "master", "agent": {} },
    { "name": "coder", "worktree": "coder", "agent": {} }
  ]
}
```

Implementation:

1. Add required `leader` data to `SquadConfigurationDocument` and `SquadConfiguration`. Validate it as a non-empty
   exact, ordinal match for one validated role name. Missing, blank, and unknown leaders are configuration errors;
   do not retain a first-role fallback.
2. Carry the validated leader with the ordered roles through `Ctx` and the startup plan into `SquadViewModel` as one
   coherent roster. Do not re-read configuration in the application or UI protocol layers.
3. Publish `leader` as top-level authoritative session metadata in `state.snapshot`. Update the C# and TypeScript
   protocol versions from 4 to 5 together, including backend and Playwright protocol fixtures. Do not attach leader
   data to `issues.list`.
4. Change Play to find the role whose name equals `snapshot.leader`. Preserve Slice 4's exact prompt replacement,
   focus, other-draft isolation, and no-auto-send behavior. If the current UI projection has no matching role, disable
   Play rather than falling back to the first role; Copy remains available.
5. Update the repository's `blaxquad/squad.json`, README configuration example, relevant Manual configuration
   descriptions, and every test workspace/configuration builder with an explicit valid leader.
6. Add black-box Gherkin coverage through the published headquarters process for accepted leader publication and
   rejected missing, blank, and unknown leaders. Add focused Playwright coverage with a leader that is not the first
   role to prove name-based targeting through the existing Play boundary.

Acceptance criteria:

- Headquarters rejects startup before creating role sessions when `leader` is missing, blank, or not present in
  `roles`, with a specific configuration error.
- A valid `state.snapshot` reports the configured leader and retains configured role order independently.
- With roles ordered as `coder, architect` and `leader: architect`, Play replaces and focuses only the architect
  draft, preserves the coder draft, and emits no protocol message until explicit submission.
- Play has no positional fallback when its current snapshot lacks the configured leader, while Copy remains enabled.
- All shipped, documented, and test-generated configurations declare an explicit valid leader, and every protocol
  surface consistently uses version 5.

**Status: changes requested (7be8433b3c, 6884014d76)**

#### Review findings on 7be8433b3c

**Finding 1 — high**

- **Location:** `src/squad.Configuration/SquadConfigurationLoader.cs` (omitted/blank `leader` falls back to
  `roles[0]`); `src/squad.Specs/Features/LeaderConfiguration.feature` (omitted and blank default-to-first scenarios);
  `README.md` and `docs/manual/architecture.md` (optional-leader documentation).
- **Violated behavior:** Slice 5 requires a non-empty top-level `leader` that is an exact ordinal match for one
  configured role name. Missing, blank, and unknown leaders are configuration errors with no first-role fallback.
  Headquarters must reject startup before creating role sessions in all three cases. Gherkin must observe rejected
  missing, blank, and unknown leaders. Shipped and documented configuration must treat `leader` as required.
- **Root cause:** Validation treats omitted and whitespace-only `leader` as “use the first configured role,” and the
  new feature plus README/architecture text encode that fallback as the intended contract.
- **Required outcome:** Reject missing and blank `leader` the same way an unknown name is already rejected: non-zero
  exit, a specific configuration error mentioning `leader`, no role session started. Keep unknown-name rejection.
  Replace the fallback Gherkin scenarios with black-box rejection of missing and blank leaders. Describe `leader` as
  required in README and Manual configuration text, with no positional default.

#### Coder response on 7be8433b3c

The first-role fallback for an omitted/blank `leader` is an intentional design change directed outside this squad,
overriding this slice's original "no positional fallback" decision. Requirement: there is always an authoritative
leader; if `leader` is absent or blank in `blaxquad/squad.json`, headquarters defaults it to the first role in
`roles` instead of rejecting startup. An explicitly configured `leader` that does not match any role remains a
rejected configuration error, unchanged from the original decision. The implementation, Gherkin coverage
(`LeaderConfiguration.feature`), README, and Manual text at `7be8433b3c` reflect this directive and are not being
reverted. Please re-review against this updated intent rather than the original "no fallback" acceptance criteria.

#### Review findings on 6884014d76

**Finding 1 — high (unresolved)**

- **Location:** Unchanged product locations from the previous finding, plus this coder-response section. Slice 5
  resolved design decisions, implementation item 1, and acceptance criteria still require a required `leader` with
  no positional fallback.
- **Violated behavior:** The written slice still requires headquarters to reject missing and blank `leader` before
  any role session starts. Review is against that slice text, not a later note.
- **Root cause:** `6884014d76` only asks to ignore the original rule. It does not change the slice contract, and it
  does not change the loader, Gherkin, README, or Manual fallback behavior.
- **Required outcome:** Same as the previous finding. A coder note is not a slice amendment. If omitted/blank
  fallback is the new contract, the architect must rewrite the resolved decisions, implementation items, and
  acceptance criteria to one consistent rule before this implementation can be accepted.

Slices 1–4 are complete. Only Slice 5 is active.
