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
The desktop timeout editor accepts finite ordinary decimal values from 0 through 300 seconds. Invalid, negative,
infinite, scientific-notation or out-of-range input is marked before Save and cannot be persisted.

## 7. Settings and runtime state

Store user files beneath `%LocalAppData%\MediaLock\`:

- `settings.json`: durable preferences, routing defaults, input and startup options.
- `state.json`: last mode, last target and Session Fingerprint used for crash recovery.
- `logs\`: bounded diagnostic logs.

Writes must be atomic enough that interruption cannot replace a valid file with partial JSON. Corrupt files yield
an actionable error and safe defaults; they are not silently overwritten.

A successful Settings save is an application-wide commit, not only a file write. Before reporting success or closing
Settings, Media Lock must persist the validated snapshot, apply it to every running consumer and publish application
state containing the same snapshot. Priority Rules, Recovery timeout and Fallback Policy update the active Router
immediately; changing the timeout during Recovery replaces the outstanding deadline. Desktop lifecycle, global-key
interception, Playback State Lock override behavior, language and theme likewise observe the committed values without
a process restart. A failed runtime or platform update leaves Settings open, reports an actionable failure and attempts
to restore the previous durable and platform values.

Every future setting must identify its runtime consumer and application time. Runtime-applicable settings require an
observable immediate-application test; an intentionally startup-only setting must say so explicitly in the UI and
specification rather than silently deferring its effect.

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

Settings ends with one About and diagnostics card. It shows the executable version, Windows product/display/build
and architecture, prerelease/stable state, and whether the executable contains an Authenticode signature. It can
copy a privacy-safe diagnostic summary, open the bounded log directory, open support, or open the canonical bug
form. The summary contains environment, routing, catalog, interception and Recovery facts, but excludes media title,
artist, account name, full path, complete settings and persisted target identity. Media-key interception distinguishes
an active hook from an enabled setting whose hook is unavailable. The summary uses the host operating system's native
line separator and explicitly asks the user to review the text before sharing.

Successful informational confirmations in Settings are transient: they clear after five seconds, clear immediately
when Settings closes, and refresh to the active UI language while visible. Actionable failures remain visible instead
of disappearing on a timer.

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
- Keep user-triggered diagnostic export privacy-safe by construction; never require raw logs or complete settings
  merely to obtain the standard summary.

## 10. Explicit non-goals for MVP

- Reliably identifying a browser tab URL such as `music.youtube.com`.
- Browser DevTools Protocol or extension integration.
- Volume, mute, customizable shortcuts and metadata/URL-based rule conditions.
- Cross-platform operation.
- Requiring elevation to broaden interception coverage without a separate reviewed decision.

## 11. Later versions

### v0.2

App Lock, ordered application Priority Rules, artwork, timeline/seek, global media-key interception and richer Recovery.

The first packaged candidate was `0.2.0-rc.1`, containing App Lock and ordered Priority Rules. The second candidate,
`0.2.0-rc.2`, adds the completed localization and visual foundation, routed-target artwork and timeline, absolute Seek,
and production global media-key interception. The third candidate, `0.2.0-rc.3`, adds the in-app About surface and
privacy-safe diagnostic summary without broadening the routing feature set. Customizable shortcuts and volume remain
separately scoped; candidate versioning does not imply those unfinished features are present.

### v0.3

Playback State Lock and a feasibility-gated Windows Media Surface Mirror. Optional Chromium and Firefox adapters that
correlate browser tabs with GSMTC Sessions remain later `v0.3.x` work when technically feasible.

Before browser integration, Phase 7 establishes localization and visual foundations as independently reviewed
post-RC work. Localization does not alter Session matching or routing semantics.

The published stable `0.2.0` artifact and retained `release/0.2` hotfix baseline remain frozen. Executable Phase 11
work targets `0.3.0` and does not transfer stable-release evidence.

#### Playback State Lock

Playback State Lock is an explicit two-state control on the current-target surface:

- **Off** sends ordinary one-shot media commands and performs no later correction.
- **Keep Playing** can be armed only while the routed target is Playing and corrects an externally observed Paused
  state with an explicit Play request.

Enforcement never uses TogglePlayPause because a delayed or duplicate toggle could invert the intended state. A Media
Lock Pause, TogglePlayPause or Stop action clears Keep Playing before routing, so an explicit Media Lock action can
pause or stop normally. Play, Next and Previous preserve it. Off ignores both external Playing-to-Paused and
Paused-to-Playing changes. Keep Playing does not resurrect a Stopped, Closed or unavailable source, including a
naturally exhausted queue.

Windows lock-screen media controls are an explicit user override. A Paused, Stopped or Closed observation from the
Armed Playback Target while the workstation is locked, or in the first fresh observation immediately after unlock,
clears Keep Playing without sending Play. Locking and unlocking without changing playback preserves the policy; the
post-unlock GSMTC refresh closes this attribution window before later desktop observations are evaluated normally.

Windows Power Suspend is a separate safety boundary. Entering sleep turns Keep Playing Off immediately. Resume may
reacquire the catalog and routing target, but it never restarts audio or automatically re-arms the policy. The user may
start playback and explicitly enable Keep Playing again after wake.

An enabled-by-default repeated-pause override gives a person another deliberate escape path. The defaults are three
distinct Playing-to-Paused transitions within five seconds and one system notification sound. The threshold event is
not corrected: it turns Keep Playing Off and leaves the target paused. Settings allow a 1–60 second window, a 2–10
transition threshold and sound on/off. Changing/buffering observations, duplicate Paused events, Recovery, catalog
loss, target changes, Media Lock commands and lock-screen overrides do not increment the sequence. Because GSMTC does
not expose the origin of a Paused value, a player that reports sustained buffering as a genuine direct
Playing-to-Paused transition cannot be distinguished perfectly; Media Lock uses the explicit Changing state and
transition history rather than claiming source attribution it does not receive.

The lock is armed against the active target identity at the moment the user selects it. Recovery may resume enforcement
only for the accepted successor of that same target. Catalog loss and ambiguous Recovery suspend correction; Windows
Power Suspend clears the policy as described above.
In Windows Auto and Priority Rules, temporary disappearance of the Armed Playback Target also suspends correction even
when the Router temporarily exposes a competing or stale Active Target. Enforcement resumes only when exactly one
fingerprint-acceptable successor becomes the Active Target. If the original Session still exists while the Active Target
changes, Keep Playing clears; a competing or fallback Session never receives a correction. Corrections wait for fresh
catalog observations, allow at most two unconfirmed Play attempts for one paused episode, and expose an actionable
Failed state instead of fighting the player indefinitely. A fresh Playing observation confirms recovery and resets the
bounded attempt state.

The first version is process-lifetime state and is not restored at login or application restart. This prevents a
background startup from unexpectedly starting audio. Persisted automatic re-arming, if ever added, requires a separate
opt-in product decision and migration. The current-target surface shows Off or Keep Playing so the policy is not
hidden in Settings.
Settings schema v7 persists only repeated-pause override preferences; the active policy and its counter remain
process-lifetime state. Schema v1–v6 documents migrate to the enabled 5-second/3-transition/sound defaults.

#### Windows Media Surface Mirror

The documented GSMTC manager can observe Windows Current Session but cannot set it. Media Lock therefore does not
promise to replace Windows' current-session selection or force the native media flyout to follow a target.

Phase 11 first probes a Media Lock-owned SMTC Media Session created through the documented desktop interop boundary.
The session mirrors the routed target's title, artist, artwork, playback state, timeline and supported controls, while
buttons and seek requests received from Windows enter the same serialized Media Lock routing path. The Media Lock-owned
session must be excluded from discovery and target selection, and every received action must route at most once without
feeding back into itself.

The feature ships only if named Windows builds reliably surface the mirror and lifecycle evidence proves that target
changes, Recovery, suspend/resume and shutdown cannot leave stale metadata or a route loop. Otherwise the result is
documented as limited or rejected; a separate Media Lock-owned on-screen display may later provide guaranteed visual
feedback, but it must not be described as the Windows native media surface.

#### Installable Windows package

Media Lock `0.3.x` may add an unsigned per-user installer beside, not in place of, the supported portable ZIP. The
installer must require no elevation, use a stable `%LocalAppData%\Programs\MediaLock\` path, create one current-user
Start Menu entry for Windows Search, register one Installed apps uninstall entry and preserve user data by default.
Installer and ZIP must contain the same reviewed payload and carry independent hashes tied to the same source commit.

Login startup remains an explicit Settings choice. In-place upgrades must preserve its exact executable command, while
uninstall may remove only a matching installed-path value and must not disturb a portable copy. Until a trusted signing
path is separately approved, documentation must identify both installer and executable as unsigned and must not imply
that installer format suppresses SmartScreen or Smart App Control. Portable distribution remains available until
clean-Windows upgrade, rollback and uninstall evidence passes. The installer permits same-version repair and upgrades,
but blocks a complete release version older than the registered installation with an actionable message so it cannot
silently expose persisted settings to an older schema.

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
