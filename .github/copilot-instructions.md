
## Coding 

- Name private instance fields with the `my` prefix followed by PascalCase, for example `myDaemonDirectory`.
- Keep one top-level C# type per source file. Extract a type when it has an independent responsibility or public/internal surface.
- Do not put default implementations in interfaces. Every interface member must be explicitly implemented by each implementation.
- Do not add unnecessary "" when using namespaces.

## Design

- Favor KISS: give every module one cohesive responsibility and one reason to change.
  Separate independent state, lifecycle, I/O, persistence, and presentation concerns behind narrow APIs.
  Composition roots only wire their owners; split a module when a second independent concern appears, and do not add pass-through layers.
- Keep authoritative session/domain state, command validation, persistence, and protocol delivery in C#;
  keep presentation, browser behavior, transient UI state, and client-side cache/protocol reconciliation in Vue.
  Never duplicate an authoritative rule across both sides; cross the boundary through explicit typed messages.

## Testing

- Cover behavior changes with the existing black-box Gherkin acceptance suite.
- Cover frontend behavior with focused black-box Playwright specs and shared test support.
  Before changing protocol, timing, scrolling, or reactive state, characterize its observable ordering and state-preservation invariants.
- Add only high-value tests that protect supported behavior or meaningful regressions.
  Do not add tests whose sole purpose is proving that removed functionality is unavailable.

## Debugging

- Never cover up or patch symptoms - always identify and address the root cause.
