# Phase 3 Manual Smoke — 2026-08-22

## Environment

- Windows 11 Pro 10.0.26200 (build 26200)
- .NET SDK 10.0.400
- Media Lock branch `codex/feat/phase-3-tray-settings-lifecycle`
- Media source: Brave YouTube video
- Build: `Release`, `net10.0-windows10.0.26100.0`

## Procedure and result

1. Launch `MediaLock.App.exe` and open **Settings** from the main-window toolbar.
   - Expected: one responsive main window; Settings opens as a separate owned window with a visible transition.
   - Actual: pass. The toolbar entry opened Settings, the window transition was visible, and both windows remained
     responsive. The Settings accessibility tree exposed the two desktop checkboxes, routing mode, recovery timeout,
     fallback policy and Save action.
2. Save Close-to-tray, login-startup and recovery options, then reopen Settings.
   - Expected: values persist in `%LOCALAPPDATA%\MediaLock\settings.json`; routing and recovery options clearly state
     that they apply after restart.
   - Actual: pass. The reopened controls matched the saved values, and the restart/Phase 4 disclosure was visible.
3. Close the main window while **Close the main window to the notification area** is enabled.
   - Expected: the window hides but the process and notification-area icon remain available.
   - Actual: pass. The window disappeared, exactly one `MediaLock.App` process remained, and its pinned notification-area
     menu could reopen the application.
4. Launch a second `MediaLock.App.exe` while the first instance is hidden.
   - Expected: the existing instance activates and the second process exits.
   - Actual: pass. The existing main window returned and process inspection still reported exactly one instance.
5. Enable **Start Media Lock when I sign in to Windows** and save.
   - Expected: the current-user Run value contains the exact quoted executable plus `--startup`.
   - Actual: pass. `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\MediaLock` contained the Release executable
     path followed by `--startup`; no administrator prompt occurred.
6. Disable **Start Media Lock when I sign in to Windows** and save.
   - Expected: the Run value is removed and `desktop.startWithWindows` becomes `false`.
   - Actual: pass. Registry inspection reported `RUN_VALUE_EXISTS=False`, while `settings.json` retained
     `closeToTray: true` and stored `startWithWindows: false`.
7. Inspect persisted state and bounded diagnostics.
   - Expected: settings, runtime state and JSONL diagnostics exist; diagnostic records omit media title and artist.
   - Actual: pass. `settings.json`, `state.json` and `logs/medialock.jsonl` existed under `%LOCALAPPDATA%\MediaLock`.
     A case-insensitive scan for title/artist fields returned zero matches. Media metadata observed in the UI was not
     copied into this evidence record.
8. Choose **Exit** from the pinned notification-area menu and inspect the process list.
   - Expected: asynchronous application cleanup completes, the tray icon disappears and no `MediaLock.App` process
     remains.
   - Actual: pass. The user selected Exit from the production notification-area menu; immediate process inspection
     reported `PROCESS_EXISTS=False`, with no shutdown-error dialog reported.

## Evidence log

The final verification completed at `2026-08-22 11:17:17 +08:00`. Registry inspection reported
`RUN_VALUE_EXISTS=False`; settings inspection reported `startWithWindows: false`; process inspection after Exit
reported `PROCESS_EXISTS=False`; and the privacy scan reported `PRIVACY_MATCH_COUNT=0`.

The UI portions used the production Release executable. Session media text was intentionally excluded from this
document and from the diagnostics assertion.
