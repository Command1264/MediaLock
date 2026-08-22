# Phase 5A App Lock manual smoke

Use the production WPF build with Brave ordinary YouTube and the Brave YouTube Music PWA. Record the Windows build,
browser version, commit and privacy-safe diagnostic lines.

## App Lock selection and routing

1. Start playback in YouTube Music and ordinary YouTube.
2. Select YouTube Music in Media Lock and choose `Lock app`.
3. Verify the main window and notification-area menu show `App Locked`.
4. Make ordinary YouTube the Windows Current Session, then route Play/Pause once from Media Lock.
5. Verify only YouTube Music changes once.

## Recovery and startup restore

1. Refresh or restart the App Locked source and verify Media Lock enters `Recovering` while no matching Session is
   available, then returns to `App Locked` when it reappears.
2. Set Default routing mode to App Lock and save.
3. Exit through the notification-area menu and verify no Media Lock process remains.
4. Relaunch with the saved source available and verify it returns to `App Locked`.
5. Route one command and verify it affects only the restored source once.

## Result

- Environment: Windows 25H2 build 26200.9168; Brave 151.1.93.137; Phase 5A production Release build from the
  `codex/feat/phase-5-app-lock` Worktree.
- Selection/routing: pass. The main window exposed distinct `Lock session` and `Lock app` actions. Selecting the
  YouTube Music PWA source and choosing `Lock app` displayed `App Locked`. Two deliberate Play/Pause commands
  changed YouTube Music from Paused to Playing and back once each while ordinary Brave YouTube remained Playing.
  Diagnostics recorded `LockedApplication` without title or artist properties.
- Recovery: pass. Refreshing the YouTube Music PWA moved App Lock to `Recovering`, then back to `App Locked`
  after the recreated Session appeared. The first post-recovery command changed YouTube Music once; ordinary Brave
  YouTube did not change, and no error or crash occurred.
- Startup restore: pass. Settings exposed `AppLock` as a default routing mode and closed after saving. Explicit
  notification-area Exit left no Media Lock process. Relaunch first published safe Windows Auto state and then
  restored `App Locked` to YouTube Music without an error or crash.
