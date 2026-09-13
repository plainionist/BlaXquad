## Knowledge base discovery

- Use `docs/manual` as the primary source for stable domain, architecture, testing, and process knowledge.
- The manual provides concepts and background; detailed behavior rules live in Gherkin `.feature` files in `**/*.Specs` assemblies.
- Do not use `docs/issues` as stable project knowledge unless the current task explicitly refers to an issue.
- When scope is unclear, search `docs/manual` with domain keywords before searching the wider repository.
- Read only the smallest relevant documents.
- If manual guidance conflicts with the current task or observed code, report the conflict before making changes.

## Coding

- Name private instance fields with the `my` prefix followed by PascalCase, for example `myDaemonDirectory`.
- Keep one top-level C# type per source file. Extract a type when it has an independent responsibility or public/internal surface.
- Do not put default implementations in interfaces. Every interface member must be explicitly implemented by each implementation.
- Do not add unnecessary `global::` qualifiers to namespace references.
- Always use curly braces for all control structures, even if they contain a single statement.

## Design

- Favor KISS: give each module one cohesive responsibility and one reason to change. Separate independent state,
  lifecycle, I/O, persistence, and presentation concerns behind narrow APIs. Composition roots only wire their
  owners; split a module when a second independent concern appears, and do not add pass-through layers.
- Make every value object valid by construction. Document its semantic invariant and enforce it in every public
  constructor or factory with `Contract.Requires`; do not rely only on callers. String-backed identities reject
  null, empty, and whitespace unless blank is explicitly meaningful, and preserve accepted input without silently
  trimming, normalizing, or changing case.
- Do not use a value type when `default(T)` would violate its invariant. Prefer a sealed immutable record or class
  for non-defaultable values; use a struct only when its default value is valid. Untyped boundaries must still
  validate malformed input explicitly and preserve their established diagnostics before constructing the value
  object; constructor contracts do not replace boundary validation.
- Keep authoritative session/domain state, command validation, persistence, and protocol delivery in C#;
  keep presentation, browser behavior, transient UI state, and client-side cache/protocol reconciliation in Vue.
  Never duplicate an authoritative rule across both sides; cross the boundary through explicit typed messages.
- Do not introduce custom exceptions unless there is a clear benefit.

## Testing

- Cover behavior changes with the existing black-box Gherkin acceptance suite.
- Cover frontend behavior with focused black-box Playwright specs and shared test support.
  Before changing protocol, timing, scrolling, or reactive state, characterize its observable ordering and
  state-preservation invariants.
- Add only high-value tests that protect supported behavior or meaningful regressions.
  Do not add tests whose sole purpose is proving that removed functionality is unavailable.

## Debugging

- Never cover up or patch symptoms - always identify and address the root cause.
