# Media Lock Architecture

## 1. Architectural style

Media Lock uses MVVM only in the WPF presentation shell. Its core is a UI-independent, state-machine-driven
application with ports for media discovery/control, input, persistence, time and operating-system lifecycle.

```text
WPF Views
    ↕ binding
ViewModels
    ↓ commands / projections
Application Services
    ↓ serialized intents
Media Router + Router State Machine
    ↓ ports
GSMTC | Win32 Input | Tray | JSON | Startup adapters
```

ViewModels project application state and submit user intents. They do not match Session fingerprints, choose
fallbacks, subscribe directly to GSMTC or persist JSON.

## 2. Project boundaries

The intended solution shape begins with a Console Prototype and grows into:

```text
MediaLock.sln
├─ MediaLock.Core
│  ├─ Media
│  ├─ Routing
│  ├─ Rules
│  └─ Configuration
├─ MediaLock.Windows
│  ├─ Gsmtc
│  ├─ Input
│  ├─ Lifecycle
│  ├─ Persistence
│  └─ Startup
├─ MediaLock.App
│  ├─ Views
│  ├─ ViewModels
│  └─ Tray
├─ MediaLock.Probe
└─ MediaLock.Tests
```

- **Core** owns Media Command, Routing Mode, Route Decision, Session Fingerprint, Recovery and Fallback Policy.
- **Windows** translates WinRT/Win32 behavior into Core ports and owns platform lifetimes.
- **App** owns WPF composition, binding and tray presentation.
- **Probe** is the Phase 0 executable for technical validation, not production UI.
- **Tests** cover Core deterministically and adapters with integration or hardware-assisted tests.

`MediaLock.Application` is a UI-independent coordination module between Core and presentation. It consumes the
catalog stream, owns Recovery deadline effects and exposes immutable application state plus application-level
intents. This keeps both WPF and Windows adapters outside Core without duplicating orchestration in ViewModels.

Dependency direction points inward: Application depends on Core; App depends on Application, Core and Windows;
Windows implements ports owned by Core or Application and therefore may depend on both. Core never depends on the
outer projects, and Application never depends on WPF or Windows implementations.

## 3. Core ports

Names may evolve during implementation, but responsibilities remain separate:

```csharp
public interface IMediaSessionCatalog;
public interface IMediaController;
public interface IMediaInputSource;
public interface ISettingsRepository;
public interface IRuntimeStateRepository;
public interface IClock;
public interface ISystemLifecycle;
```

`IMediaSessionCatalog` publishes immutable snapshots and change notifications. `IMediaController` attempts a Media
Command against a resolved handle and reports supported, succeeded or failed. `IMediaInputSource` emits a command
only after its backend has determined whether the underlying input was consumed.

Phase 1 exposes routing through the deliberately small `IMediaRouter.DispatchAsync(RouterIntent,
CancellationToken)` interface. A result contains the new immutable `RouterState`, one `RouteDecision`, and explicit
deadline effects; callers execute those effects but do not decide when to schedule or cancel Recovery. They also do
not rank candidates, retain live Session objects, execute recovery policy, or coordinate concurrent intents.
`IMediaController` is the platform adapter seam used by the router after it has resolved exactly one target.

Phase 2 uses `IMediaSessionCatalog.WatchAsync` as the catalog seam. The production `GsmtcMediaAdapter` implements
both this interface and `IMediaController`, keeping the ephemeral-key-to-live-Session map local to one deep Windows
module. The application module is the sole owner of that adapter and of `IMediaRouter` disposal.

## 4. State model

Routing state is explicit and immutable at the Core boundary. A reducer-like transition function accepts the prior
state plus an intent/event and returns a new state with effects to execute.

```text
WindowsAuto
   ├─ LockApp ───────────────▶ AppLocked
   ├─ UsePriorityRules ──────▶ PriorityRules
   └─ LockSession ───────────▶ SessionLocked
                                  │ SessionLost
                                  ▼
                              Recovering
                               │       │
                         Recovered   PolicyDecision
                               │       │
                               ▼       ▼
                         SessionLocked Fallback/Waiting
```

Effects such as GSMTC calls, persistence and logging run outside the transition function and feed their result back
as events. This makes race handling and tests deterministic without replacing WPF MVVM with an MVC architecture.

## 5. Command routing

For every Media Command:

1. Capture the input and determine whether the backend consumed the original action.
2. Snapshot the current routing state and Sessions.
3. Calculate one Route Decision.
4. Attempt the command only against the resolved target.
5. Record outcome and update observable state when needed.

A consumed input must not also fall through to Windows default processing. Phase 0 must measure this behavior for
each candidate backend (`WM_APPCOMMAND`, raw input, hooks or other justified mechanism) rather than assuming event
observation implies suppression.

Phase 8C promotes the Phase 0 `WH_KEYBOARD_LL` backend into the Windows adapter. Its dedicated message-loop thread
maps only Play/Pause, Previous, Next and Stop. The hook callback performs no GSMTC, persistence or logging work: it
asks `MediaInputCoordinator` for a synchronous accept/pass-through decision and caches that decision through repeated
KeyDown and Key-up messages. Accepted commands enter a bounded single-reader queue; a full queue passes the original
key through to Windows.

Acceptance snapshots the resolved target and advertised capability. The queued Route intent carries that expected
Session Key, and the Router skips the command if its Active Target changed before execution. This may intentionally
drop a consumed command during a target race, but it cannot redirect that command to a competing player. Settings
schema v6 adds an enabled-by-default interception preference; disabling it changes the callback decision immediately
without tearing down the hook. Hook startup/runtime failures are diagnostic and degrade to Windows handling rather
than terminating GSMTC routing or the UI.
The application publishes each immutable state reference with volatile read/write semantics, and the coordinator
publishes its stopped state atomically, so the Hook thread observes settings, target and shutdown transitions without
mixing revisions or retaining a thread-local stale snapshot.

## 6. Session lifecycle

The Windows adapter obtains `GlobalSystemMediaTransportControlsSessionManager`, enumerates Sessions, and listens to
manager and Session events. Each refresh creates an immutable snapshot for Core. Adapter code owns subscriptions;
reacquisition or shutdown unsubscribes before discarding old manager and Session objects.

On suspend/resume or adapter failure:

1. Mark the catalog unavailable without discarding the Locked Target descriptor.
2. Dispose old subscriptions.
3. Reacquire the manager and publish a full snapshot.
4. Submit recovery evaluation to the serialized router queue.

The adapter publishes `Suspended`, `Reacquiring`, `Available` and `Unavailable` catalog states. Resume performs at
most three attempts with bounded delays (immediate, 500 ms and 2 s). Each attempt releases partial manager state
before retrying. Exhaustion does not terminate the catalog stream, so a later resume can reacquire. Application state
and privacy-safe `catalog.status` diagnostics project these outcomes without retaining title or artist.

## 7. Concurrency

All router intents are serialized through one application-owned queue or dispatcher. Platform callbacks perform
minimal work and enqueue events. UI state is projected onto the WPF dispatcher. Cancellation and shutdown are
explicit; retries are bounded and observable.

The Phase 1 router owns a single-reader intent queue. Submission order is preserved across callers, queued
cancellation completes promptly without terminating the queue, and disposal cancels in-flight work before draining
the closed queue. Catalog intents carry an immutable array and identical refreshes are idempotent. A Recovery epoch
stays stable across unrelated refreshes but is cleared after recovery, so the active deadline remains bounded while
a stale timeout cannot override a target that has already recovered.

The Phase 2 application dispatcher keeps router dispatch and result/effect projection in the same serialized
critical section, so asynchronous continuations cannot publish an older revision after a newer one. A terminal
catalog publishes an empty snapshot before its error, clearing stale live targets and entering normal Recovery.
The GSMTC adapter uses one refresh worker with a capacity-one coalescing signal; event bursts therefore request at
most one follow-up refresh instead of creating an unbounded task backlog. Adapter lifetime cancellation interrupts
an in-flight Session read before shutdown waits for the worker.

Phase 3 keeps desktop lifecycle composition at the WPF application root. A current-user named semaphore identifies
the primary process and a current-user named pipe activates its window. The primary creates persistence, startup,
diagnostic, GSMTC, application and presentation components in that order. Explicit shutdown first removes the tray
surface, then disposes presentation/application resources and finally releases instance coordination.
The main-window toolbar and tray both open one owned settings window through ViewModel navigation callbacks. WPF
window transitions use short opacity animations only when Windows client-area animations are enabled.

## 8. Persistence and diagnostics

Settings and runtime state use separate repositories and files. Persistence uses replace-on-success semantics:
serialize to a sibling temporary file, flush, then atomically replace where supported. Schema versions permit future
migration.

Structured logs include timestamps, state transitions, anonymizable Session source data, route reasons and control
outcomes. Normal logs minimize title and artist retention; an explicit diagnostic mode may add metadata with clear
user disclosure and bounded retention.

Phase 3 stores schema-versioned `settings.json` and `state.json` beneath `%LocalAppData%\MediaLock\` with sibling
temporary files and replace-on-success writes. A corrupt settings file produces safe defaults and remains untouched;
if the user later saves replacement settings, the original is first copied to `settings.corrupt.<timestamp>.json`.
Runtime state is saved after serialized router transitions. Startup restores App Lock by submitting the saved source
application identity through the same router interface as an interactive App Lock. Session Lock restores only when
the saved default mode requests it and fingerprint matching produces one acceptable, unambiguous candidate;
Windows Auto never restores a saved lock. JSONL diagnostics rotate to at most three one-megabyte files and omit title/artist
unless a future explicitly disclosed diagnostic mode supplies them.
Phase 10B adds a read-only environment adapter at `IAppEnvironmentInfoProvider`. Its Windows implementation owns
Registry, runtime-architecture, entry-assembly version and embedded Authenticode-certificate inspection, including
normalizing the stale `Windows 10` Registry product name when build 22000 or later identifies Windows 11. The pure
`DiagnosticSummary` module combines that immutable environment snapshot with `MediaLockApplicationState`; it emits
only an invariant allowlist of support facts and never traverses media metadata, target fingerprints, file paths or
the full settings document.

User-triggered desktop effects cross the single-method `IDesktopSupportActions` seam. Its Windows adapter owns
Clipboard, Shell and `%LocalAppData%\MediaLock\logs` behavior plus the canonical GitHub support URLs. Settings
ViewModel supplies diagnostic text only for the copy action, catches adapter failures as localized actionable UI
errors and otherwise remains independent of Registry, Clipboard and process launch details.
Loaded Recovery timeout and Fallback Policy configure the router before its first catalog snapshot. Recovery,
fallback and Priority Rule edits take effect on the next process start. A successful explicit main-window Routing
Mode intent performs the router transition first, saves any required Locked Target runtime state, then commits the
corresponding startup setting last inside the same serialized application dispatch. A failed transition or target
save leaves the prior startup setting intact; a settings save failure keeps the current-run transition observable,
restores the previously persisted runtime document, retains the prior startup setting and reports an actionable
error. Tray Windows Auto is a process-lifetime override: runtime-state autosaves remain suppressed until a durable
main-window mode choice resumes them, so later commands and catalog updates cannot erase the saved lock target.
Settings projects that startup mode as read-only state instead of exposing a second mode selector; a mode-only state
update preserves any unsaved Settings edits.

Phase 5B stores ordered `PriorityRule` values in settings schema v3. The router owns rule evaluation behind
`IMediaRouter.DispatchAsync`: it skips disabled rules, selects the first source application with a current Session,
and delegates same-application choice to the App Lock candidate policy. With no match, it uses Windows Current
Session without changing to Windows Auto. Priority Rules have no Locked Target and therefore need no runtime-state
identity; settings schema v1/v2 migrate to an empty rule list.

Phase 7A advances settings to schema v4 by adding a desktop UI-language preference. Schema v1-v3 documents migrate
to the Windows-language choice. The App project owns the localization module, culture resolution and WPF markup
extension; Core stores and validates only the neutral preference values. Presentation strings, enum descriptions,
accessibility names and notification-area labels resolve through the same resource manager. One culture is selected
after settings load and before any ViewModel, window or tray surface is created. A successful Settings save publishes
one App-layer culture change that refreshes existing WPF bindings, ViewModel projections and notification-area menu
labels without restarting routing, GSMTC discovery or input interception. A failed save does not change culture.

Phase 7B advances settings to schema v5 with a neutral App-owned theme preference; schema v1-v4 documents migrate
to Windows theme while preserving every previously supported setting. `UiTheme` resolves Windows, Light and Dark
preferences and swaps one palette resource dictionary beneath a stable shared control-style dictionary. Views consume
semantic dynamic resources, so a successful Settings save refreshes existing windows without reconstructing
ViewModels or platform services. When Windows theme is selected, the App composition root observes Windows preference
changes and reapplies the resolved palette. The main-window frame and notification-area menu remain Windows-owned;
the presentation shell maps the resolved theme to the supported DWM immersive-dark frame attribute without exposing
Win32 dependencies outside App. Settings is one fixed-size owned modal WPF surface with transparent rounded chrome,
an explicit drag region and Cancel/Escape commands that restore the persisted ViewModel projection before closing.
`ShowDialog` keeps the owner natively disabled and returns only after Windows has restored owner activation, avoiding
an intervening third-party foreground window after Alt+Tab. Motion stays in the presentation shell, respects
`SystemParameters.ClientAreaAnimation` and never delays routing.

The presentation shell's reusable component geometry, semantic states and Windows-owned surface boundaries are
defined in `docs/ui-design-language.md`. `Themes/Controls.xaml` is the implementation seam for reusable WPF chrome;
individual Views own composition and domain-specific variants rather than independent native-looking templates.

Phase 8C advances settings to schema v6 with the global-media-key interception preference. Schema v1-v5 documents
migrate to enabled, preserving the product's established default and every prior desktop preference.

Phase 7C extends the immutable Session snapshot with optional, encoded presentation artwork. The Windows GSMTC
Session adapter reads only bounded JPEG or PNG thumbnail payloads and caches the result until a media-properties
change; unreadable artwork becomes absent rather than failing the catalog refresh. Core does not decode images and
artwork does not participate in Session fingerprints or candidate ranking. The App converts the encoded payload to
a frozen, size-constrained WPF image and otherwise shows a neutral placeholder.

The existing immutable GSMTC timeline remains the single observation boundary. The Main ViewModel derives a
read-only position from the routed target and a supplied `TimeProvider`: Playing advances by elapsed wall time,
non-playing states remain at the observed position, and all values clamp to valid Start/End bounds. A presentation
timer only requests property refresh; it never writes an estimated position into Core, dispatches routing intents or
survives the window lifetime. Seek remains outside the command model until separate hardware/player evidence exists.

Phase 8A keeps Seek inside the disposable Probe. A small immutable request parses invariant seconds and validates the
absolute position against the selected live Session's current timeline before the Probe calls
`TryChangePlaybackPositionAsync(TimeSpan.Ticks)`. The Probe reports capability, API acceptance and observed position as
separate facts. No parameterized command crosses into Core, Application, production Windows adapters or WPF.

Phase 8B deepens the existing Media Command value instead of adding a parallel Seek interface. Transport actions and
absolute Seek share `ApplicationIntent.Route`, `RouterIntent.Route` and `IMediaController.TryExecuteAsync`, so target
resolution, Recovery, capability checks, serialization and Route Decision semantics remain local to the Router module.
Core validates the live timeline and absolute bounds before the Windows adapter translates the position to GSMTC ticks.

The WPF timeline owns only a gesture preview. One completed mouse, touch or keyboard gesture submits one Media Command.
An accepted request is pending presentation state, not a new routing state: the preview yields only when a later catalog
snapshot confirms the requested position. A bounded presentation timeout, target change or command failure restores the
latest observed timeline. No optimistic position is written into Core or persisted.

The Main ViewModel owns a presentation-only selection bookmark independently from Routing Mode. It first preserves an
exact selected Session Key. If Windows replaces that ephemeral Key during catalog reconstruction, the presentation
carries selection forward only when the old source-application identity has exactly one candidate. Missing or ambiguous
candidates keep the list unselected until the configured Recovery timeout; timeout clears the bookmark without selecting
the first row. A direct user selection replaces it.

When the bookmarked row was the resolved Locked Target immediately before App Lock or Session Lock entered Recovery,
the Router's successor Active Target takes precedence over the generic unique-source match. Core Recovery remains the
sole authority for target identity; the UI bookmark never changes Router state.

## 9. Phase 11 playback state and Windows media surface

### Playback State Lock

Playback State Lock extends the existing deep Application routing module rather than adding a ViewModel loop or a
second media-control service. Core owns pure Off/Keep Playing eligibility and correction decisions. The Application
dispatcher owns the Armed Playback Target, observation/confirmation status and bounded correction effects inside the
same serialized critical section as catalog and Route intents.

Each correction is an ordinary explicit Play Media Command carrying the captured `ExpectedTarget`. The Router
still resolves capability and rejects stale targets; the Windows GSMTC adapter remains a one-shot controller. Catalog
observations trigger policy evaluation, so no polling loop is introduced. Recovery, catalog unavailability and suspend
make enforcement Suspended; fallback, an unrelated Active Target and shutdown cannot redirect a correction. Only the
Router-accepted successor of the armed identity may resume enforcement.

Application state exposes the selected policy, armed target continuity and Ready, Suspended or Failed result for WPF
projection. The current-target ViewModel submits intents and renders state but neither compares playback observations
nor retries commands. The module waits for a fresh playback observation before any subsequent correction, allows at
most two unconfirmed Play attempts per paused episode and exposes Failed when confirmation is exhausted. Media Lock
Pause, TogglePlayPause and Stop clear the policy before their one-shot command is routed.

The Core lifecycle port separately exposes workstation Lock/Unlock without depending on Windows APIs. The Windows
adapter translates Session Switch notifications and requests a fresh GSMTC snapshot after unlock. Application keeps a
small attribution window across that transition: Playing closes it and preserves Keep Playing, while Paused, Stopped
or Closed clears the policy without correction. A missing or unknown target remains Suspended and cannot redirect a
command to a competitor.

Settings schema v7 adds `PlaybackStateLockSettings` for the repeated-pause escape hatch. Application keeps a bounded
queue of distinct direct Playing-to-Paused observation times. Duplicate Paused events never add entries; Changing,
Recovery, suspend, target changes, explicit routes, settings changes and workstation transitions reset or bypass the
sequence. Reaching the configured threshold publishes a one-shot Released status and performs no Play correction.
WPF owns the replaceable notification-sound adapter and the five-second localized live-region message; neither Core
nor Application depends on presentation or audio APIs. Schema v1–v6 migration supplies the 5-second/3-transition,
sound-enabled defaults.

### Windows Media Surface Mirror

The GSMTC manager's documented contract exposes current-session observation, not current-session mutation. Per
[ADR 0003](adr/0003-probe-a-media-lock-owned-smtc-mirror.md), Phase 11B therefore uses a replaceable Windows adapter
to probe a Media Lock-owned SMTC Media Session through `ISystemMediaTransportControlsInterop.GetForWindow`.

```text
Routed target snapshot ──▶ Application mirror projection ──▶ Windows SMTC mirror
                                                                  │ button/seek
                                                                  ▼
                                                        serialized Application intent
                                                                  │
                                                                  ▼
                                                            existing Router
```

The projection contains bounded presentation metadata, playback/timeline observations and capabilities only; it does
not become routing identity. Windows button and seek events are translated to the existing Media Command path with the
current captured target. The mirror's own Session identity is filtered before catalog snapshots reach Core, preventing
self-selection, recursion and duplicated routing. Adapter disposal disables the mirror, removes event subscriptions
and clears published state before application shutdown.

Phase 11B remains a disposable feasibility adapter until tests show whether supported Windows builds select and render
it predictably. A production seam is justified only when both a Windows implementation and deterministic fake are used
by Application tests. Failure to influence Windows' current-session choice is a probe result, not a reason to introduce
undocumented API calls or move routing policy into the Windows project.

## 10. Composition

Application startup is the composition root. It creates adapters, repositories, router and ViewModels through
constructor injection, enforces single-instance behavior, and starts services in a defined order. Shutdown stops
input first, drains or cancels routing work, persists state, removes subscriptions and then exits the tray process.
The same root injects the Windows environment and desktop-support adapters into Settings; tests replace both through
their public seams without launching Explorer, a browser or the Clipboard.

An installer-only `--uninstall-cleanup` command is handled before single-instance, GSMTC, tray or input initialization.
It delegates to the Windows startup adapter, which removes the current-user Run value only when its complete quoted
command matches the executing installed path. Missing or portable-owned values are preserved. The command produces no
desktop UI and reports cleanup failure through its process exit code for the uninstaller.

## 11. Publication

The release candidate targets `win-x64`, self-contained, single-file publication. Single-file output can be larger
and may interact with native libraries or extraction behavior, so build success, cold start, tray resources,
settings paths and clean-machine execution are release gates rather than assumptions.
