# Phase 2 WPF Shell

Phase 2 adds the first production vertical slice from Windows GSMTC to the WPF presentation shell. It deliberately
does not include physical-media-key interception, tray behavior, persistence, startup registration or
suspend/resume reacquisition.

## Module interfaces

- Phase 2 originally used `IMediaSessionCatalog`／`IMediaController`. Phase 16C supersedes those ports with
  `IMediaTargetCatalog`／`IMediaTargetController`; `GsmtcMediaAdapter` still uses the same ephemeral `SessionKey`
  internally for discovery and control without exposing live WinRT objects.
- `IMediaLockApplication` owns catalog consumption, the Core router and Recovery deadline effects. UI callers start
  it once, submit one `ApplicationIntent` at a time and observe immutable `MediaLockApplicationState` notifications.
  It serializes dispatch with result/effect publication and turns a terminal catalog into an empty snapshot plus an
  observable error, so stale Sessions cannot remain routable.
- `MainWindowViewModel` is the WPF binding interface. It projects state, exposes capability-aware async commands and
  converts application or route failures into an actionable error state. It never references GSMTC.

## WPF behavior

The main window shows routing status, resolved target, discovered Media Sessions, Lock and Windows Auto actions,
and Play／Pause／Toggle／Previous／Next／Stop controls. Empty, Recovering and error states are visible. Interactive
controls have explicit UI Automation names and participate in normal keyboard tab navigation.

Run the shell from the Phase 2 Worktree with:

```powershell
dotnet run --project src\MediaLock.App\MediaLock.App.csproj --configuration Release
```

## Validation boundary

Application and ViewModel behavior is deterministic through in-memory adapters. The Windows adapter lifecycle and
ephemeral-key routing are tested across its public Core interfaces with the WinRT boundary replaced by a fake.
Actual GSMTC interoperability, WPF binding and keyboard/accessibility behavior require a short Windows manual smoke
test after automated checks pass.

The first passing production-boundary record is in `manual-smoke-2026-08-22.md`.
