---
title: Separate logical code blocks with empty lines
priority: 2
---

# Separate logical code blocks with empty lines

## Goal

Apply the repository coding rule that logical code blocks are separated with a single empty line to all
hand-maintained code. This is a formatting-only change and must not alter behavior.

## Scope

- Audit backend C# across every production assembly.
- Audit frontend Vue, TypeScript, and CSS, including composables, protocol code, components, and Playwright support.
- Audit all test code and specifications, including Gherkin feature files, bindings and support, fakes, and
  Playwright specs.
- Audit hand-maintained PowerShell, shell, and other executable source in the repository.
- Do not edit generated output under `bin`, `obj`, `dist`, `node_modules`, or Playwright result directories.

## Formatting rule

- Put one whitespace-free empty line between adjacent groups of statements that have distinct purposes, such as
  setup, validation, construction, execution, state publication, cleanup, and assertion.
- Separate a completed control-flow block from the next logically distinct operation.
- Keep one cohesive operation together, including multiline expressions, argument lists, fluent chains, and
  tightly related guard statements.
- Do not combine this cleanup with renaming, restructuring, behavior changes, or unrelated formatting.

## Acceptance criteria

- All hand-maintained backend, frontend, test, fake, and script code has been reviewed against the rule in
  `.github/copilot-instructions.md`.
- Distinct logical blocks are separated consistently by exactly one empty line, and added empty lines contain no
  spaces or tabs.
- Product behavior, public contracts, generated output, and test expectations are unchanged.
- The full .NET build and test suite, frontend build, and complete Playwright suite pass.