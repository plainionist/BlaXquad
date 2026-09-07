---
title: Add a headless UI protocol host
priority: 11
---

# Add a headless UI protocol host

This issue implements the [backend test strategy](../manual/test-strategy.md).

## Goal

Allow the actual `squad-hq` process to be driven through the real versioned UI JSON protocol without starting Photino
or Vue.

Add a headless `IWindowHost` implementation selected explicitly by headquarters. It must own the same
`UiProtocolSession` used by the visual host and transport newline-delimited JSON over standard input and standard
output.

This is a supported transport adapter, not a test hook. Photino remains the default UI.

## Acceptance criteria

- `squad-hq launch` can explicitly select a headless stdio UI mode.
- The headless host passes every input message through `UiProtocolSession`.
- Protocol output is written as one JSON message per stdout line.
- Process diagnostics are written to stderr and cannot corrupt the protocol stream.
- Startup waits for the normal UI-ready protocol command.
- Session-start notification, snapshots, transcript synchronization, updates, pages, and protocol errors use the same
  code paths as the Photino host.
- Host-control shutdown, cancellation, input closure, and disposal terminate the headless host cleanly.
- The default Photino launch path and protocol behavior are unchanged.
- No public message-injection method, serialized-message callback, or nullable test collaborator is added.
