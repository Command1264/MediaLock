# Phase 5B Priority Rules manual smoke

Use the production WPF Release build with Brave ordinary YouTube and the Brave YouTube Music PWA. Record the
Windows build, browser version, commit and privacy-safe diagnostic lines.

## Ordered routing

1. Start playback in YouTube Music and ordinary YouTube.
2. Open Settings. Add the YouTube Music PWA source as the first enabled rule and ordinary Brave as the second.
3. Save the rules, select `Priority Rules` on the main window, explicitly Exit, then relaunch.
4. Verify the main window, notification-area menu and read-only Settings startup summary show `Priority Rules`.
5. Route Play/Pause once and verify only YouTube Music changes once.

## Reordering, disabling and fallback

1. Disable the YouTube Music rule or move ordinary Brave above it, save, Exit and relaunch.
2. Route Play/Pause once and verify only ordinary YouTube changes once.
3. Make every enabled rule unavailable while a different Windows Current Session remains available.
4. Route Play/Pause once and verify that current Session changes once while the UI remains `Priority Rules`.
5. Re-enable and restore the intended ordering, save, Exit and relaunch; verify the first available rule wins again.

## Result

- Environment: Windows 25H2 build 26200.9168; Brave 151.1.93.137; Phase 5B production Release build from the
  `codex/feat/phase-5-priority-rules` Worktree.
- Settings surface: pass. The editable Default Routing Mode selector was removed after manual testing exposed that
  an interactive main-window mode and its separate startup default could diverge. The replacement read-only summary
  displayed `Priority Rules` without clipping; rule editing remained unchanged.
- Startup mode persistence: pass. Switching from Priority Rules to Windows Auto changed `settings.json` to
  `windowsAuto`; switching back changed it to `priorityRules` while preserving both rules. Two subsequent process
  restarts opened directly in Priority Rules without the stale App Lock warning.
- Ordered command routing: pending human-assisted Play/Pause verification using the persisted main-window mode.
- Reordering/disabling: covered automatically through the public Settings ViewModel seam; production UI check pending
  with ordered routing.
- Windows Current Session fallback: pass. With no saved rules, the main window remained `Priority Rules`; one
  Play/Pause changed the then-current ordinary Brave Session once while YouTube Music remained paused. When that
  ordinary Session later disappeared, the displayed target changed to the new Windows Current Session as designed.
- Errors or crashes: none. A manual smoke attempt exposed repeated identical source-list refreshes clearing pending
  ComboBox selection; the regression was fixed and covered by a collection-stability test before this record.
