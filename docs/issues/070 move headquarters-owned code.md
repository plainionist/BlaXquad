---
title: Move headquarters-owned code into squad-hq
priority: 5
---

# Move headquarters-owned code into squad-hq

Move the following headquarters-only code from `squad.Core` to `squad-hq`:

## Host control

- `HostLease` and `IHostLease`;
- `CleanupLease`;
- `HostControlClient`; and
- `HostControlRequest`.

This includes host locking, metadata, stale-state cleanup, and the local readiness/shutdown protocol.

## Configuration loading

- `SquadConfigurationLoader` and `SquadConfigurationException`;
- `SquadConfiguration`, `SquadRoleConfiguration`, and `SquadAgentConfiguration`; and
- the configuration document types used for deserialization.

## Acceptance criteria

- The listed code and its namespaces are moved to `squad-hq`.
- Types not required across assembly boundaries are no longer public.
- Existing black-box Gherkin scenarios for configuration, host ownership, readiness, startup, and shutdown remain
  green.

## Why priority 5

This removes headquarters-only code from the shared core before the other refactorings begin.