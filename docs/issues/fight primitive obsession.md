---
title: fight primitive obsession
priority: 1
---

do we still have dictionaries with string as key? 
what is that string? can we use one of the domain value objects like roleId etc?
should we introduce some enum?

strings in general - not only dictionary but also parameters and return values - which should be converted to enums in the heart of the app?

code needs to be explicit!

not only check product code also the specs assembly

if we keep strings this needs to be justified!
example: it probably makes sense to keep strings in the protocol to the UI

## Current-state audit (2026-09-12)

The relevant distinction is not "string bad". A string is appropriate while it represents text or an external
encoding. It needs replacing when application code already knows that it represents one closed choice or one
specific kind of identity. Parse at an input boundary, keep the typed value through the owning code, and format it
again only at an output boundary.

### Remaining cases

#### Permission mode

`permissions` is the closed set `prompt | approveAll`, but
[`AgentSettings`](../../src/squad.Domain/AgentSettings.cs) and
[`SquadAgentConfiguration`](../../src/squad.Configuration/SquadAgentConfiguration.cs) retain it as `string` after
configuration validation. [`CopilotSdkClient`](../../src/squad.AgentProvider.CopilotSdk/CopilotSdkClient.cs) then
branches on the protocol spelling `"approveAll"`.

- Introduce a `PermissionMode` enum next to `ReceiveMode`.
- Parse the JSON spelling in `SquadConfigurationLoader`, carry the enum through the domain and provider context,
	and map it to provider behavior in the Copilot adapter.
- Keep `SquadConfigurationAgentDocument.Permissions` as `string?`: it is the JSON input DTO and must also represent
	unsupported values so validation can report them.

