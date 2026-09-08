---
title: Migrate host ownership specifications
priority: 15
---

# Migrate host ownership specifications

This issue implements the [backend test strategy](../manual/test-strategy.md).

## Goal

Rewrite `HostOwnership.feature` around the behavior of the actual `squad-hq` processes and commands rather than
`HostLease`, `HostControlClient`, metadata structures, or raw named-pipe requests.

Retain operator-visible behavior such as exclusive launch, readiness waiting, timeout diagnostics, equivalent project
paths, shutdown, stale-host recovery, and idempotency. Replace internal lease and metadata assertions with observable
process outcomes. Delete malformed-pipe and exact-metadata scenarios when they do not represent a supported public
contract.

## Acceptance criteria

- Every retained scenario runs one or more actual `squad-hq` processes.
- Duplicate launch is observed through command failure while the first host remains usable.
- Readiness and busy/unknown-role outcomes are observed through `squad-hq wait-for-agent`.
- Shutdown is requested through `squad-hq shutdown`, including path-equivalence and idempotency cases.
- Stale ownership is proven by successfully starting or controlling a replacement host, not by inspecting cleanup
  leases.
- Step definitions do not construct `HostLease`, `HostControlClient`, or `NamedPipeClientStream`.
- Tests do not read `PipeName` or assert the exact shape of `.blaxquad/host.json`.
- Low-level scenarios without a user-visible or compatibility contract are deleted rather than preserved through new
  test hooks.
- Host ownership behavior and diagnostics remain covered on every supported platform.
