
## Knowledge base discovery

- Primary knowledge base location: `docs/Manual`.
- Use `docs/Manual` for stable project knowledge (domain, architecture, testing, process).
- `docs/Manual` explains concepts/background; detailed behavior rules live in Gherkin `.feature` files inside `**/*.Specs` assemblies.
- Do not use `docs/issues` as stable project knowledge unless the current task explicitly refers to an issue.
- When scope is unclear, search `docs/Manual` with domain keywords before broad repository search.
- Prefer the smallest relevant document(s). Do not load unrelated manual pages into context.
- If manual guidance conflicts with the current task or observed code, report the conflict before making changes.

## Coding 

- Name private instance fields with the `my` prefix followed by PascalCase, for example `myDaemonDirectory`.
- Keep one top-level C# type per source file. Extract a type when it has an independent responsibility or public/internal surface.
- Do not put default implementations in interfaces. Every interface member must be explicitly implemented by each implementation.
- Do not add unnecessary "" when using namespaces.
- Always use curly braces for all control structures, even if they contain a single statement.

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
