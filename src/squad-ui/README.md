# BlaXquad UI

The dashboard is a Vue 3 client hosted by the Photino desktop application.

## Ownership

| Area | Owner |
|---|---|
| Application composition and command wiring | `src/composables/useDashboardSession.ts` |
| Presentational markup and browser interaction | `src/App.vue` and `src/components/` |
| Host transport and protocol contracts | `src/protocol/bridge.ts` and `src/protocol/messages.ts` |
| Transcript synchronization, history, and announcements | `src/composables/useTranscript*.ts` |
| Transcript indexing, geometry, anchors, and classification | `src/transcript/` |
| Authoritative session state and transcript persistence | C# projects under `../` |
| Native window lifecycle, commands, and delivery | `../squad.Photino/PhotinoWindowHost.cs`, `PhotinoUiCommandHandler.cs`, and `PhotinoUiDeliveryCoordinator.cs` |

Dependencies point inward from components to composables, then to protocol
contracts and transcript modules. The transport depends only on protocol
contracts and browser host APIs; it does not depend on components.

## Protocol boundary

The UI exchanges versioned envelopes defined in `src/protocol/messages.ts`.
C# owns command validation, authoritative session state, archive persistence,
truncation, and delivery ordering. Vue owns presentation and transient client
state. Archived transcript responses are reconciled by
`src/transcript/resolveArchivedEntry.ts` through the transcript history/feed
composables.

## Validation

From `src/squad-ui`:

```powershell
npm run build
npm run test:browser
npm run test:browser -- transcript-protocol.spec.ts
npm run test:browser -- transcript-scrolling.spec.ts transcript-virtualization.spec.ts
```

From the repository root:

```powershell
dotnet test src\squad.Specs\squad.Specs.csproj --no-restore --disable-build-servers --nologo --verbosity minimal
```
