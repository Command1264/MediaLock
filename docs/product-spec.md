# Media Lock Product Specification

## 1. Product intent

Media Lock is a Windows media-control router. It lets Windows continue discovering every compatible Media Session
while giving the user explicit control over which Session receives a Media Command.

The product succeeds when it reliably finds a Session, locks it, prevents competing applications from stealing
the physical media-key action, survives target loss, and restores the lock when an appropriate successor appears.

## 2. Users and primary scenario

The primary user regularly plays music in one application or browser Session while other applications also
publish media. They want physical media keys to remain attached to the chosen source without closing other media
or maintaining external AutoHotkey and PowerShell scripts.

Primary acceptance scenario:

1. YouTube Music is playing in Brave.
2. The user locks that Media Session.
3. Spotify or Discord begins playing and Windows Current Session changes.
4. The user presses Play/Pause or Next.
5. Only the Locked Target receives the Media Command.

## 3. Routing modes

### Windows Auto

Resolve the target from Windows Current Session at command time. Media Lock does not preserve a target choice,
but may still surface Session information and manual controls.

### App Lock

Prefer Media Sessions whose source application matches the Locked Target. If multiple Sessions belong to the same
application, apply an explicit deterministic candidate policy; App Lock does not claim browser-tab precision.

### Session Lock

Preserve the selected Media Session as the Locked Target even when Windows Current Session changes. When the live
Session disappears, transition to Recovery instead of retaining a dead object reference.

## 4. MVP — v0.1

The MVP includes:

1. Request and enumerate GSMTC Sessions.
2. Display source application, title, artist and playback status.
3. React to manager and Session change events.
4. Select, lock and unlock a Media Session.
5. Support Windows Auto and Session Lock.
6. Route Play, Pause, TogglePlayPause, Previous, Next and Stop where supported.
7. Capture and consume supported physical media keys through a replaceable backend.
8. Detect target loss and expose Locked, Recovering and fallback status.
9. Recover an equivalent or same-application Session according to policy.
10. Fall back to Windows Current Session when configured.
11. Provide a WPF main window and system-tray control surface.
12. Close to tray, single-instance operation and optional login startup.
13. Persist settings separately from recoverable runtime state.
14. Write diagnostic logs without media-control failures crashing the UI.
15. Handle suspend/resume by reacquiring the manager and re-evaluating the Locked Target.

Phase 0 must first prove that at least one feasible input backend can capture and consume the target hardware media
keys without duplicate Windows dispatch under ordinary user privileges. If it cannot, the MVP input promise and
supported-device scope must be revised before GUI implementation.

## 5. Session observation

For each available Session, collect available values including:

- `SourceAppUserModelId`
- playback status and supported controls
- title, artist, album title, track number and playback type
- timeline position and bounds

Prefer `SessionsChanged`, `CurrentSessionChanged`, `MediaPropertiesChanged`, `PlaybackInfoChanged` and
`TimelinePropertiesChanged` over continuous polling. Event handlers schedule refresh work; they do not directly
mutate UI-bound collections from arbitrary threads.

## 6. Identity and Recovery

A live GSMTC Session object is ephemeral and must not be persisted as identity. A Session Fingerprint combines
stable source information with observed characteristics and timestamps. Track metadata can influence candidate
confidence but must not define identity because it changes during normal playback.

When a Locked Target disappears:

1. Record target loss and enter Recovering.
2. Observe new and changed Sessions.
3. Rank candidates using an explicit, testable matching policy.
4. Re-lock only when the policy yields an acceptable candidate.
5. Otherwise apply the configured Fallback Policy.

The default product proposal is: wait up to 15 seconds for a suitable successor, then consider a same-application
Session, then use Windows Current Session. This remains a configurable policy rather than hard-coded behavior.

## 7. Settings and runtime state

Store user files beneath `%LocalAppData%\MediaLock\`:

- `settings.json`: durable preferences, routing defaults, input and startup options.
- `state.json`: last mode, last target and Session Fingerprint used for crash recovery.
- `logs\`: bounded diagnostic logs.

Writes must be atomic enough that interruption cannot replace a valid file with partial JSON. Corrupt files yield
an actionable error and safe defaults; they are not silently overwritten.

At startup, Default Windows Auto ignores any previously saved lock. Default Session Lock restores only a valid
persisted Session Lock whose fingerprint has one acceptable, unambiguous catalog successor. Missing, corrupt,
expired or ambiguous state remains safely unbound and observable rather than selecting a guess.

## 8. UI and tray behavior

The main window shows the current target, routing status, media controls, discovered Sessions and lock actions.
Closing the window hides it when close-to-tray is enabled; only an explicit Exit command terminates the process.
Tray state distinguishes Windows Auto, Locked, Recovering, Suspended, Reacquiring and Unavailable, and provides
essential controls without opening the window.

## 9. Non-functional requirements

- Run without administrator privileges in supported scenarios.
- Keep Core free of WPF and direct WinRT/Win32 dependencies.
- Serialize state transitions so events and key input cannot race the router into contradictory states.
- Unsubscribe platform events and release resources during shutdown or adapter restart.
- Make all failed control attempts observable through return values and structured logs.
- Avoid logging sensitive or excessive media metadata by default; document diagnostic modes before enabling them.

## 10. Explicit non-goals for MVP

- Reliably identifying a browser tab URL such as `music.youtube.com`.
- Browser DevTools Protocol or extension integration.
- Volume, mute, artwork, seek UI and customizable rule engine.
- Cross-platform operation.
- Requiring elevation to broaden interception coverage without a separate reviewed decision.

## 11. Later versions

### v0.2

App Lock, automatic priority rules, customizable shortcuts, artwork, timeline/seek, volume and richer Recovery.

### v0.3

Optional Chromium and Firefox adapters that correlate browser tabs with GSMTC Sessions when technically feasible.

### v1.0

Stabilized Session Lock, App Lock, rules, browser integration, tray control, recovery, startup, logging and settings
import/export, distributed as a portable Windows package.

## 12. Success criteria

The MVP is not complete until relevant automated tests pass and the supported hardware/application matrix proves:

1. Sessions are discovered and updated.
2. A selected Session stays the control target.
3. Other media applications do not receive the consumed key action.
4. Session loss does not corrupt state or crash the application.
5. A suitable returning Session can be recovered according to policy.
