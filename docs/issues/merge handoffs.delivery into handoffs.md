---
title: merge handoffs.delivery into handoffs
priority: 2
---

make it a subfolder in handoffs assembly

## Plan

### Slice 1: Fold handoff delivery into `squad.Handoffs`

**Logical change:** Make Headquarters-side handoff delivery a cohesive submodule of the existing handoff assembly
instead of a separately built and referenced assembly.

**Implementation**

1. Move `HandoffDeliveryLog`, `HandoffDeliveryService`, `InProcessHandoffPoller`, and `IRoleNotifier` into
   `src/squad.Handoffs/Delivery/`, retaining the `squad.Handoffs.Delivery` namespace and existing public contracts so
   runtime callers require no source-level API migration.
2. Transfer the delivery project's `squad.Configuration` dependency to `squad.Handoffs`; replace
   `squad.Runtime`'s delivery-project reference with a reference to `squad.Handoffs`; and remove the now-redundant
   delivery-project reference from `squad-hq`.
3. Remove `squad.Handoffs.Delivery` from `squad.slnx` and delete its project after its source files have moved, leaving
   no build, project, or packaging reference to the former assembly.
4. Update `docs/Manual/modules.md` so `squad.Handoffs` documents both the file-backed handoff primitives and
   Headquarters-side delivery responsibility, with no standalone delivery module.

This is one atomic slice: moving the sources without rewiring the project graph would break consumers, while removing
the project before moving its implementation would remove supported delivery behavior.

**Acceptance criteria**

- The solution builds without a `squad.Handoffs.Delivery` project or output assembly, and no project or solution
  reference points at its former path.
- Delivery types reside under `src/squad.Handoffs/Delivery/` and retain the `squad.Handoffs.Delivery` namespace and
  current behavior.
- The existing `Delivering handoffs`, `Handoff queues are launch-scoped, not restart-safe`, `Creating outbound
  handoffs`, and `Surfacing a real handoff-pump failure after readiness` acceptance features pass unchanged.
- The module inventory describes one `squad.Handoffs` assembly owning both handoff storage primitives and delivery.
