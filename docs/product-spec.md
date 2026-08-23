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

### Priority Rules

Evaluate an ordered list of enabled source-application identities and use the first application that currently has
an available Media Session. Within one application, use the same playing, recency and stable-key candidate policy
as App Lock. If no rule matches, resolve Windows Current Session at command time. Priority Rules do not inspect
track metadata, browser URLs or tabs, and duplicate source-application identities are invalid settings.

### App Lock

Prefer Media Sessions whose source application matches the Locked Target. If multiple Sessions belong to the same
application, prefer a playing Session, then the most recently observed Session, then stable Session-key order.
App Lock does not claim song, browser-URL or browser-tab precision.

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

The desktop settings persist a UI language preference independently from routing state. Supported choices are
Windows language, English (`en-US`) and Traditional Chinese (`zh-TW`). Windows-language selection uses Traditional
Chinese for a Traditional-Chinese Windows UI and otherwise falls back to English. Language changes take effect on
successful Settings save across the existing WPF windows, notification-area surface and presentation projections.
Language choices display `English` and `繁體中文` as language-native names regardless of the current UI culture.

The desktop settings also persist a WPF client-area theme preference: Windows theme, Light or Dark. Windows-theme
selection reads the current Windows app-theme preference at startup and reacts to later Windows preference changes.
A successful Settings save applies the selected theme immediately; a failed save leaves the current theme unchanged.
The main-window frame remains Windows-owned and its caption follows the resolved Light or Dark client theme on
supported Windows 11 builds. Settings uses a fixed-size, owned, modal, frameless rounded surface with an explicit
draggable header; it has no minimize, maximize or native close actions, and its owner cannot be manipulated while it
is open. Cancel and Escape discard unsaved edits, while a successful save closes the surface. Closing Settings
returns focus directly to the main window even after the user has switched applications.

Every successful explicit Routing Mode choice on the main window becomes the startup Routing Mode. Merely selecting
a Session or sending a Media Command does not change it. Settings shows this startup choice as read-only state while
remaining the editing surface for Recovery, desktop behavior and Priority Rules. A failed startup-mode or Locked
Target persistence attempt keeps the prior startup choice and remains observable instead of creating an invalid
durable lock.

At startup, saved Windows Auto ignores any previously saved lock. Saved App Lock restores a valid persisted source
application and resolves its current candidate with the same deterministic policy used for an interactive App Lock;
it enters Recovery when that application has no current Session. Saved Priority Rules loads its ordered settings
without requiring a persisted Locked Target. Saved Session Lock restores only a valid
persisted Session Lock whose fingerprint has one acceptable, unambiguous catalog successor. Missing, corrupt,
expired or ambiguous state remains safely unbound and observable rather than selecting a guess.

## 8. UI and tray behavior

The main window shows the current target, routing status, media controls, discovered Sessions and lock actions. Its
four Routing Mode actions expose exactly one checked mode with a fixed-size marker, accent border and themed selection
surface; the status line remains the detailed Ready, Recovery or Unavailable signal.
If the selected list item was the Locked Target when Recovery began, the list remains unselected while that target is
absent and selects the Router-resolved successor when it returns. An explicit user selection during Recovery cancels
that presentation-only selection recovery.
Independently of Routing Mode, the list keeps a presentation-only bookmark for its last explicit or initial selection.
The row remains selected across catalog reconstruction when its Session Key is unchanged or when exactly one replacement
has the same source-application identity. While no successor exists the list remains unselected; a successor returning
within the configured Recovery timeout restores selection. An explicit selection replaces the bookmark. Timeout or
ambiguous same-application replacements leave the list unselected rather than choosing the first row. This presentation
continuity does not change the Locked Target or routing target.
Closing the window hides it when close-to-tray is enabled; only an explicit Exit command terminates the process.
Tray state distinguishes Windows Auto, Locked, Recovering, Suspended, Reacquiring and Unavailable, and provides
essential controls without opening the window.

The WPF client area uses one shared semantic visual system for Light and Dark themes. Routing status, current target,
Session selection and primary media action have distinct visual hierarchy without changing their command semantics.
Motion is short, non-blocking and disabled when Windows client-area animation is disabled.

The current-target surface may show optional Now Playing artwork and a read-only timeline. Both are projections of
the Session that would receive a command at that moment; selecting a different list row does not change them. Artwork
failure falls back to a neutral placeholder and never changes routing state. Timeline interpolation is presentation
only, clamps to the last observed GSMTC bounds and resets when the routed target disappears or changes. The progress
indicator is not seekable until player-specific GSMTC capability and acceptance evidence is documented.

Phase 8A may request absolute playback positions only from the disposable Console Probe. It does not add Seek to the
production router, input backend, persisted settings or WPF interaction model. A Session advertising playback-position
support and returning `true` are necessary observations, not sufficient evidence of movement; the requested and
observed timelines must also be compared on each supported player.

Phase 8B makes the routed target's valid timeline seekable when that Media Session advertises playback-position
support. A completed pointer or keyboard gesture submits one absolute-position Media Command through the same Router
as transport commands. Dragging is local preview only. GSMTC acceptance retains the preview temporarily, while a later
timeline snapshot remains authoritative; rejection, failure, target loss or confirmation timeout restores the observed
position with an actionable error. Seek adds no physical-key binding, setting or persisted state.
Pressing an empty point on the timeline may continue directly into a captured drag; only release commits the final
previewed position.
The main-window error card provides an explicit dismiss action; errors do not disappear on an arbitrary timer.

The desktop settings persist whether Media Lock intercepts global media keys. It defaults to enabled so the installed
application fulfils its routing promise; disabling it takes effect immediately and passes physical media keys through
to Windows without restarting discovery or routing.

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
- Volume, mute, customizable shortcuts and metadata/URL-based rule conditions.
- Cross-platform operation.
- Requiring elevation to broaden interception coverage without a separate reviewed decision.

## 11. Later versions

### v0.2

App Lock, ordered application Priority Rules, customizable shortcuts, artwork, timeline/seek, volume and richer Recovery.

The first packaged candidate is `0.2.0-rc.1` because the current product already includes App Lock and ordered
Priority Rules. Customizable shortcuts, artwork, timeline/seek, volume and richer Recovery remain separately scoped;
the candidate version does not imply those unfinished features are present.

### v0.3

Optional Chromium and Firefox adapters that correlate browser tabs with GSMTC Sessions when technically feasible.

Before browser integration, Phase 7 establishes localization and visual foundations as independently reviewed
post-RC work. Localization does not alter Session matching or routing semantics.

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
