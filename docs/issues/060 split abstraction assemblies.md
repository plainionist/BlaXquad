---
title: Split abstraction assemblies by responsibility
priority: 60
---

# Split abstraction assemblies by responsibility

The current abstractions assembly combines contracts for agent providers, presentation adapters, platform hosting,
application-domain collaboration, and concrete infrastructure helpers. This makes unrelated implementations depend on
the same project and obscures which side owns each contract.

## Target architecture

Replace the general-purpose abstractions assembly with narrowly owned contract assemblies. Assembly and namespace
names must describe the architectural boundary rather than a particular implementation.

### Agent provider abstractions

The agent provider abstraction assembly defines the provider-neutral boundary through which the application creates,
controls, and observes agent backends and sessions. It owns:

- backend and session lifecycle interfaces;
- provider selection and creation contracts that expose only provider concerns;
- provider-neutral startup and role context values;
- agent event, request, response, readiness, failure, and usage contracts; and
- transport-neutral primitives required to publish or consume those contracts.

Provider implementations such as the Copilot SDK adapter depend on this assembly. The assembly must not depend on a
provider implementation, UI framework, desktop host, application ViewModel, or composition root. Provider factory
contracts must not decide which window technology is used or expose implementation-specific diagnostic commands.

### UI abstractions

The UI abstraction assembly defines the application-facing presentation port used by desktop or other presentation
adapters. It owns:

- commands and queries that a presentation adapter can issue to the application;
- snapshot, transcript, notification, and refresh contracts exposed to presentation;
- presentation-facing interaction models; and
- UI delivery semantics that are independent of Photino, browser APIs, and the Vue implementation.

The application core implements these ports and Photino consumes them. Photino-specific transport, serialization,
window APIs, and browser behavior remain in the Photino adapter. When presentation must surface an agent interaction,
the UI boundary may depend on provider-neutral interaction contracts; the provider abstraction must never depend on
the UI abstraction.

### Hosting abstractions

Use a separate, narrow hosting abstraction assembly for substitutable process-wide platform capabilities that belong
to neither agent providers nor presentation behavior. It owns contracts for host/window lifetime, operating-system
lifetime integration such as sleep inhibition, and terminal signals needed by the application run loop.

The hosting assembly must contain contracts and small boundary value types only. Runtime composition records,
implementation selection, startup policy, and private command dispatch belong to the composition root. Concrete
process execution and operating-system helpers belong with their implementation rather than in an abstraction
assembly.

## Placement rules

- Put a contract in the assembly owned by the consumer-facing boundary whose implementations it makes replaceable.
- Keep domain state, generation identities, and collaboration interfaces used only inside the application in the core
  domain assembly.
- Keep concrete helpers and stateless utilities in the implementation or infrastructure assembly that owns them.
- Keep cross-boundary construction and implementation selection in the executable composition root.
- Place request, response, event, and option types beside the contract whose semantics they express.
- Do not retain or introduce a miscellaneous shared abstraction assembly.
- Keep dependencies one-way and reject project-reference cycles between provider, UI, hosting, core, and adapters.

The expected dependency direction is:

- application core depends on agent provider abstractions and implements UI abstractions;
- provider adapters depend on agent provider abstractions;
- presentation adapters depend on UI abstractions and hosting abstractions;
- the executable composition root depends on the selected adapters, core, and required contracts; and
- the agent provider and hosting abstractions remain independent of presentation implementations.

## Migration plan

Perform the split in the following independently buildable slices. The implementing agent should inventory individual
types as part of each slice; this plan fixes assembly ownership and migration order rather than prescribing a
class-by-class move list.

### Slice 5: Remove the catch-all assembly

- [ ] Slice 5 complete

Replace remaining references to `squad.Abstractions` with direct references to the owning contract assemblies, remove
`squad.Abstractions` from the solution, and delete the project once no consumers remain. Add architecture scenarios
that verify the allowed dependency direction and representative forbidden references, then run the existing
black-box suite to confirm the split has not changed observable behavior.

## Acceptance criteria

- The general-purpose abstractions assembly is replaced by agent provider, UI, and hosting contract assemblies with
  the responsibilities above.
- Every remaining public contract has a clear owner and a consumer outside its implementation assembly.
- Domain-only contracts and values are owned by the core domain instead of a shared contracts project.
- Concrete process, operating-system, serialization, and framework code is absent from all abstraction assemblies.
- Provider selection no longer controls presentation technology or unrelated host behavior.
- Project references enforce the documented dependency direction without cycles or implementation-to-implementation
  leakage through contract projects.
- Architecture acceptance scenarios protect the assembly boundaries and representative forbidden dependencies.
- Existing black-box Gherkin scenarios remain green with no observable change to startup, session, interaction, UI,
  relaunch, or shutdown behavior.

## Why priority 60

Clear contract ownership will make additional providers and presentation hosts safer to add, but the split touches
most project-reference boundaries. Performing it after the higher-priority responsibility refactors reduces churn
while still establishing the architecture before new adapters are introduced.