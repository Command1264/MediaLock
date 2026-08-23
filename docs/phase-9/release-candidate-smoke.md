# Phase 9 `0.2.0-rc.2` packaged validation

## Candidate identity

- Version: `0.2.0-rc.2`.
- Source commit: `aca17b40f3b6300ca4e2eeeca2590dfbbf7287a7`.
- Source dirty: `false`.
- Runtime identifier: `win-x64`.
- Self-contained: `true`.
- Single-file: `true`.
- Signed: `false`; Authenticode status `NotSigned`.
- Archive: `MediaLock-0.2.0-rc.2-win-x64.zip` (`78,663,994` bytes).
- Archive SHA-256: `0c750e7f2eec132b6b82c4d78f491f961dad76358c4e0b9c49dc3042779ec5e7`.

Independent inspection recomputed the digest, matched the manifest and checksum file, and expanded exactly one file
named `MediaLock.exe`. The executable reported ProductVersion `0.2.0-rc.2` and FileVersion `0.2.0.0`.

## Local host smoke — 2026-08-24

Environment:

- Windows 11 Pro 25H2, build `26200.9168`, x64.
- ASUS ROG STRIX FLARE mechanical keyboard.
- Brave ordinary YouTube plus Brave YouTube Music PWA exposed as separate GSMTC Sessions.
- Priority Rules resolved YouTube Music while ordinary YouTube was also playing.

Results:

1. Cold start produced one main window and discovered both Sessions with artwork, timeline and playback state: pass.
2. Settings opened as a modal surface; saving unchanged valid settings closed it and returned to the main window: pass.
3. Launching the same packaged executable again activated the existing window without creating a second window or
   process: pass.
4. The in-app Play/Pause command changed YouTube Music from Paused to Playing while ordinary YouTube remained Playing;
   a second command restored YouTube Music to Paused: pass.
5. Closing the main window hid it while one Media Lock process remained; launching the executable again restored the
   same window: pass.
6. One physical Play/Pause press changed YouTube Music while ordinary YouTube remained unchanged; the second press
   restored the starting playback state: pass, as reported by the user.
7. Notification-area `Exit` left zero candidate processes: pass.
8. `settings.json` and `state.json` remained valid JSON. The bounded JSONL log existed with zero invalid lines and no
   Error/Critical entries at inspection time: pass.

No crash, duplicate command or competing-source change was observed.

## Windows Sandbox gate

Status: pending. Transfer the ZIP, manifest and checksum only. The Sandbox result must independently verify this exact
source commit and SHA-256 before recording cold-start, settings, startup registration, tray, Edge GSMTC routing and
explicit-Exit evidence. Local-host results do not satisfy the clean-environment gate.
