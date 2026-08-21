# Phase 2 Manual Smoke — 2026-08-22

## Environment

- Windows 11 Pro 10.0.26200 (build 26200)
- Brave 151.1.93.137
- .NET SDK 10.0.400
- Media Lock branch `codex/feat/phase-2-wpf-shell`
- Media sources: Brave YouTube video and Brave installed YouTube Music app

## Procedure and result

1. Build `MediaLock.sln` in Release and launch `MediaLock.App.exe`.
   - Expected: one responsive Media Lock window.
   - Actual: pass; one window opened without an error dialog.
2. Inspect the available Media Sessions and the Windows automatic target.
   - Expected: both Brave sources appear and the current GSMTC Session resolves to one catalog entry.
   - Actual: pass; the YouTube video and YouTube Music track both appeared. Windows Auto exposed the current
     target's Play, Toggle and Stop capabilities instead of reporting it unavailable.
3. Select the YouTube Music entry and choose **Lock selected**.
   - Expected: routing changes to the selected Session and controls reflect its advertised capabilities.
   - Actual: pass; Windows Auto became available as an alternate action, and Previous and Next became enabled.
4. Invoke Play, Pause, Toggle, Next, Previous and Stop from the WPF controls.
   - Expected: each supported command routes to the locked YouTube Music Session; the ordinary YouTube video does
     not change.
   - Actual: pass. Play changed YouTube Music from Paused to Playing; Pause restored Paused; Toggle restored
     Playing; Next changed Track A to Track B while ordinary YouTube remained Paused. Previous
     left the same title, consistent with YouTube Music's restart-current-track behavior after sufficient elapsed
     time. Stop removed the YouTube Music GSMTC Session, disabled the locked controls during Recovery and then
     exposed the paused ordinary YouTube controls only after the configured Windows-current fallback.
5. Choose **Windows Auto** after fallback.
   - Expected: Session Lock ends and Windows Auto becomes the active mode.
   - Actual: pass; the Windows Auto button became disabled and the current paused YouTube target retained its
     Play, Toggle and Stop capabilities.
6. Walk the enabled controls with Tab and use Down in the Session list.
   - Expected: disabled actions are skipped; list navigation remains contained; every action has an explicit UI
     Automation name.
   - Actual: pass. Focus order was Lock selected -> available Session -> Play -> Toggle play pause -> Stop; Down
     remained on the sole available Session. The accessibility tree exposed names for Lock selected, Windows Auto,
     Available Media Sessions and all six media commands, including disabled commands.
7. Close the main window and observe the process for ten seconds.
   - Expected: asynchronous GSMTC/application cleanup completes and the process exits without a shutdown error.
   - Actual: pass; the process exited in approximately one second and a subsequent build could replace all output
     assemblies.

## Evidence log

The final run completed at `2026-08-22 03:34:18 +08:00`. UI Automation snapshots recorded both initial Brave
Sessions, each target playback-state transition, the anonymized Next track change, the Stop-induced Session removal, enabled
and disabled controls, and the keyboard focus sequence. Process observation recorded `exited` within the ten-second
shutdown window. Phase 2 has no persisted application-log subsystem; this bounded UI Automation and process record
is the production-boundary log for this smoke.
