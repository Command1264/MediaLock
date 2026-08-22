# Phase 4 manual smoke

Use Brave YouTube and the Brave YouTube Music PWA available on the test machine. Record the Windows build, browser
versions, Media Lock commit and redacted diagnostic lines for each run.

## Partial result — 2026-08-22

- Environment: Windows 25H2 build 26200.9168; Brave 151.1.93.137; Phase 4 worktree based on `d70b558` with
  uncommitted reviewed changes.
- Source: one ordinary Brave YouTube GSMTC Session; YouTube Music and ambiguity cases remain pending.
- Pass: the production WPF build enumerated Brave, locked it, and exposed the revised safe-restore disclosure in
  Settings. Saving Default Session Lock wrote the expected setting.
- Pass: after forced process termination and restart, the main UI returned directly to `Locked` with the recreated
  Brave Session. A Pause then Play round trip both returned `route.completed` with reason `LockedSession`, and the
  visible playback status changed once in each direction.
- Pass: after Windows sleep and wake, diagnostics progressed through `Suspended`, `Reacquiring`, and `Available`;
  the main UI returned to `Locked`. A Play then Pause round trip routed once in each direction to the locked Brave
  Session and restored its original paused state.
- Pass: in the rebuilt production WPF app, `Save settings` closed the Settings window while the main UI remained
  `Locked` and the Brave Session remained paused.
- Pass: refreshing the Brave YouTube Music PWA briefly removed its GSMTC Session and moved Media Lock to
  `Recovering`. Media Lock captured the recreated Session, returned to `Locked`, and the first Play/Pause command
  changed YouTube Music exactly once without changing ordinary Brave YouTube. No crash or `Unavailable` state was
  observed.
- Pass: exiting from the notification-area menu left no `MediaLock.App` process. Relaunching the production build
  restored `Locked` to YouTube Music; a Play/Pause command changed it exactly once, ordinary Brave YouTube did not
  change, and no error or crash occurred.
- Privacy check: inspected JSONL entries contained mode, status, revision, command, decision and reason only; no
  title or artist property was written.
- Not physically exercised: forcing missing and equally plausible startup candidates. Both cases are covered by
  automated startup-policy tests that verify Media Lock does not silently select an unrelated Session.

## Startup recovery

1. Set Default routing mode to Session Lock and save.
2. Lock YouTube Music, exit from the notification-area menu, then start Media Lock again while the same Session is
   present. Verify it restores `Locked` to YouTube Music.
3. Repeat after closing the target before Media Lock starts. Verify no unrelated Session is selected and the UI is
   `Recovering`, then follows the configured timeout/fallback.
4. Expose two equally plausible Brave candidates before startup. Verify neither is silently selected.
5. Set Default routing mode to Windows Auto, exit and restart. Verify a saved Session Lock is ignored.

## Browser Session recreation

1. Lock YouTube Music, refresh/restart it, and verify the UI enters `Recovering` during the gap.
2. Verify a unique successor restores `Locked` and the first media command affects it once.
3. Repeat with ordinary Brave YouTube as a competing Session; verify it does not receive the locked command.

## Windows lifecycle

1. Lock YouTube Music and put Windows to sleep once.
2. Wake and sign in. Verify the tray/main UI progresses through `Reacquiring` to `Locked` (or actionable
   `Unavailable` after three failed attempts).
3. Press Play/Pause once. Verify the target changes exactly once and competing YouTube does not change.
4. Inspect redacted JSONL diagnostics for `catalog.status` transitions; confirm no title or artist properties appear.
