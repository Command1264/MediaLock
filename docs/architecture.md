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

Dependency direction points inward: App and Windows depend on Core abstractions; Core does not depend on them.

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

## 9. Composition

Application startup is the composition root. It creates adapters, repositories, router and ViewModels through
constructor injection, enforces single-instance behavior, and starts services in a defined order. Shutdown stops
input first, drains or cancels routing work, persists state, removes subscriptions and then exits the tray process.

## 10. Publication

The release candidate targets `win-x64`, self-contained, single-file publication. Single-file output can be larger
and may interact with native libraries or extraction behavior, so build success, cold start, tray resources,
settings paths and clean-machine execution are release gates rather than assumptions.
