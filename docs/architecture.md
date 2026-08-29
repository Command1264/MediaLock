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
public interface IMediaTargetCatalog;
public interface IMediaTargetController;
public interface IMediaInputSource;
public interface ISettingsRepository;
public interface IRuntimeStateRepository;
public interface IClock;
public interface ISystemLifecycle;
```

`IMediaTargetCatalog` publishes immutable provider-qualified observations and change notifications.
`IMediaTargetController` dispatches a Media Command once to an exact target and distinguishes Succeeded, Unsupported,
Rejected, Failed and Outcome Unknown. `IMediaInputSource` emits a command
only after its backend has determined whether the underlying input was consumed.

Phase 1 exposes routing through the deliberately small `IMediaRouter.DispatchAsync(RouterIntent,
CancellationToken)` interface. A result contains the new immutable `RouterState`, one `RouteDecision`, and explicit
deadline effects; callers execute those effects but do not decide when to schedule or cancel Recovery. They also do
not rank candidates, retain live Session objects, execute recovery policy, or coordinate concurrent intents.
`IMediaTargetController` is the platform Adapter seam used by the Router after it has resolved exactly one target.

Phase 16C replaces the production catalog／control seam with `IMediaTargetCatalog.WatchAsync` and
`IMediaTargetController`. The production `GsmtcMediaAdapter` implements both, keeping the
provider-qualified-target-to-live-Session map local to one deep Windows Module. Application exposes reconciled Media
Targets plus an explicit GSMTC Sessions projection for the unchanged `0.3.0` UI and persistence behavior. The
application module is the sole owner of that Adapter and of `IMediaRouter` disposal.

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

`MediaLockApplication` owns the successful-settings-application transaction. It validates one immutable settings
snapshot, persists it, synchronizes owned platform state such as login startup, sends Router-owned values through
`RouterIntent.UpdateOptions`, and only then publishes application state with that same snapshot. Presentation and
input consumers observe the published state or their explicit post-commit callback; they do not reread
`settings.json`. If a platform or Router application step fails, the application attempts to restore the prior
durable and platform values and does not publish the candidate snapshot as successful.

Router options are a live seam. Priority Rule changes recalculate the Priority Rules target in the serialized Router
queue. A Recovery-timeout change during an active Recovery advances its epoch, cancels the old deadline and schedules
one replacement using the new duration; the eventual timeout evaluates the current Fallback Policy. New settings must
be assigned to an owning runtime module here (or be explicitly documented as startup-only) before they are exposed by
Settings.
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

Phase 17 replaces those presentation strings with the structured problem boundary defined by
[ADR 0008](adr/0008-use-structured-problems-for-user-facing-failures.md). Application owns semantic identifiers,
stable codes, severity, occurrence identity and an optional exception type. App owns exact English／Traditional
Chinese resource lookup and fallback. Core Route Decisions expose semantic reasons and at most an exception type;
they never expose localized copy or raw exception messages. `ProblemCode` is an optional structured diagnostic field,
and `DiagnosticSummary` receives the latest reported code without promoting it into active UI state. This keeps localization out of Core and private path／media／
target data out of the standard failure contract.
Loaded Recovery timeout and Fallback Policy configure the router before its first catalog snapshot. Recovery,
fallback and Priority Rule edits also update the running router immediately. A successful explicit main-window Routing
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

Phase 15 adds a presentation-only source-application metadata Seam. The Application project owns the small
`ISourceApplicationMetadataResolver` Interface; the Windows Adapter enumerates the current user's AppsFolder and maps
exact `System.AppUserModel.ID` values to Shell display names plus a distinct executable product name when available.
The App presentation Module combines that metadata with the complete set of visible source identities, adds the host
qualifier only when it contributes information, disambiguates friendly-name collisions with the exact raw identity,
and otherwise falls back to that raw identity.

The Windows Adapter removes only a trailing generic `Browser` suffix from an executable product name before exposing
the host qualifier. It does not rewrite vendor names or infer browsers from `_crx_` identifiers.

```text
GSMTC SourceAppUserModelId ──▶ Windows AppsFolder metadata Adapter
              │                              │
              └──────────────────────────────┤
                                             ▼
                             App presentation catalog
                                             │
                                             ├─▶ Main Session／target labels
                                             └─▶ Settings Priority Rules／choices
```

The presentation never enters Core, settings or runtime state. App Lock, Session Lock, Priority Rules, Recovery,
selection bookmarks and routed commands continue comparing and persisting the original `SourceAppUserModelId`.
Tooltips and accessibility help retain that raw identity even when the visible label is friendly. Missing Shell
metadata is a normal fallback result and cannot remove a Session or interrupt routing.
Unexpected AppsFolder／shortcut metadata failures emit the privacy-safe `source.metadata.failed` diagnostic with only
the lookup stage and exception type before the same raw-ID fallback is used.

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

Phase 18 generalizes that boundary to every provider-neutral Media Target and separates raw observation from projected
presentation:

```text
provider snapshots + monotonic timestamp
                  │
                  ▼
 Application catalog observation
                  │
                  ▼
 Core Playback Rate Estimator ──▶ Effective Playback Rate + source + confidence
                  │
                  ▼
 Application target projection ──▶ WPF timeline interpolation
```

`PlaybackRateEstimator` is one pure in-process Core Module with a small concrete Interface; a single implementation
does not justify an `I*` abstraction or Adapter. Each observation carries a provider-qualified `MediaTargetId`, raw
timeline, playback state, monotonic timestamp and optional Reported Playback Rate. The Module owns per-target rolling
samples, robust slope calculation, confidence, hysteresis, numerical tolerances and bounded state retention. It never
accepts a WPF-interpolated position.

The candidate uses a five-second window, at least three observations spanning three seconds, the median of all valid
pairwise slopes, 10% published-rate tolerance and two consecutive same-direction challengers. Per-target samples expire
with the window and an LRU cap retains at most 256 target states. These are private Module policy, not caller options.

Application supplies timestamps from `TimeProvider.GetTimestamp()`, projects the resolved Effective Playback Rate and
forgets estimator state when a target leaves the catalog. Composite catalog snapshots may contain cached targets from
providers that did not publish the current update, so Application fingerprints the authoritative observation fields
and preserves their previous monotonic anchor instead of sampling them again. Projected targets always cross the
provider-neutral `MediaTargetsUpdated` path, including GSMTC-only catalogs, so Router／WPF cannot lose the projection.
A valid reported value is authoritative. Missing or invalid values may use a confident estimate; otherwise the result
is the 1× fallback. Seek, non-Playing state, Recovery,
reconnect, invalid bounds, target／document replacement, non-monotonic time and discontinuous position reset the affected
target before later samples can regain confidence. Reset and projection do not alter Router state, Media Target identity,
command capability, Recovery correlation or persisted schemas. See
[ADR 0009](adr/0009-separate-reported-and-effective-playback-rate.md).

When an already-confident target produces a finite but divergent incremental slope, Core keeps it outside the rolling
window until the next observation. Continuation at the divergent slope starts a new-rate window; continuation at the
published slope classifies the intermediate position as a discontinuity and clears confidence. A bounded position
residual prevents normal quantized timelines from being mistaken for either transition.
If the trusted observations on both sides still match the published slope, Core discards only the isolated pending
sample and retains confidence.

If a cached provider observation remains unchanged for the full five-second estimator window, Application expires an
Estimated result to Fallback. A monotonic Application confidence worker checks this independently of catalog traffic,
so a completely silent provider cannot keep an estimate alive indefinitely. It rebases only the presentation timeline
to the already-displayed bounded position so the visible timeline does not jump backward; the raw provider position
remains the sole estimator input.

Phase 8A keeps Seek inside the disposable Probe. A small immutable request parses invariant seconds and validates the
absolute position against the selected live Session's current timeline before the Probe calls
`TryChangePlaybackPositionAsync(TimeSpan.Ticks)`. The Probe reports capability, API acceptance and observed position as
separate facts. No parameterized command crosses into Core, Application, production Windows adapters or WPF.

Phase 8B deepens the existing Media Command value instead of adding a parallel Seek interface. Transport actions and
absolute Seek share `ApplicationIntent.Route`, `RouterIntent.Route` and `IMediaTargetController.TryExecuteAsync`, so target
resolution, Recovery, capability checks, serialization and Route Decision semantics remain local to the Router module.
Core validates the live timeline and absolute bounds before the Windows adapter translates the position to GSMTC ticks.

The WPF timeline owns only a gesture preview. One completed mouse, touch or keyboard gesture submits one Media Command.
An accepted request is pending presentation state, not a new routing state: the preview yields only when a later catalog
snapshot confirms the requested position. A bounded presentation timeout, target change or command failure restores the
latest observed timeline. No optimistic position is written into Core or persisted.

The Main ViewModel also projects a presentation-only refresh interval from the Effective Playback Rate. While Playing
with a finite timeline, it targets at most approximately one media second per refresh and clamps the Dispatcher timer
to 50–500 milliseconds. Rate changes update that interval through ordinary ViewModel notification; Pause, target loss
or a missing timeline returns to the 500-millisecond idle cadence. This affects only WPF repaint frequency and never
changes estimator sampling, authoritative observations or command dispatch.

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
observations trigger policy evaluation, so no polling loop is introduced. Recovery and catalog unavailability make
enforcement Suspended; a real Power Suspend clears the process-lifetime policy so resume cannot restart audio.
Fallback, an unrelated Active Target and shutdown cannot redirect a correction. Locked modes reuse the Router-accepted
successor. Windows Auto and Priority Rules retain the armed fingerprint while its
Session is absent, refresh that fingerprint from live observations while it remains active, require exactly one
acceptable candidate and re-arm only when that candidate is also the Router Active Target. A changed Active Target
while the original Session still exists remains an explicit target change and clears the policy.

Application state exposes the selected policy, armed target continuity and Ready, Suspended or Failed result for WPF
projection. The current-target ViewModel submits intents and renders state but neither compares playback observations
nor retries commands. The module waits for a fresh playback observation before any subsequent correction, allows at
most two unconfirmed Play attempts per paused episode and exposes Failed when confirmation is exhausted. Media Lock
Pause, TogglePlayPause and Stop clear the policy before their one-shot command is routed.

The Core lifecycle port separately exposes workstation Lock/Unlock without depending on Windows APIs. The Windows
adapter translates Session Switch notifications and requests a fresh GSMTC snapshot after unlock. Application keeps a
small attribution window across that transition: Playing closes it and preserves Keep Playing, while Paused, Stopped
or Closed clears the policy without correction. A missing or unknown target remains Suspended and cannot redirect a
command to a competitor. Power Suspend arrives through catalog lifecycle state instead: Application clears Keep Playing
on `Suspended`, and later `Reacquiring`／Available snapshots cannot re-arm it.

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

### Accepted provider-neutral production seam

Phase 16A／16B established a proven disposable Browser Adapter, so the seam is no longer hypothetical. The
[Phase 16C gate](phase-16/production-integration-plan.md) and
[ADR 0006](adr/0006-use-provider-neutral-media-targets-in-production-routing.md) accept the neutral seam before any
production Browser Adapter is added beside GSMTC. The Router-facing interface exposes immutable provider-qualified
target identity, capabilities, observations and one-shot command results; it does not expose Extension IDs, Chrome
tab IDs, frame IDs, document IDs, DOM selectors, site permissions or Native Messaging envelopes. Those facts remain
inside one deep Browser Adapter Module.

```text
Browser profile + explicit permission + Page Binding
                         │
                         ▼
             Browser Adapter Module
                 │              │
                 │ identity     │ live resolution
                 ▼              ▼
        Browser Media Target   Browser Media Endpoint
                 │              │
                 └──── command ─┘
                         │
                         ▼
               neutral media-target seam
```

A Page Binding is Extension-issued and authoritative; URL, title, artist and browser executable are never sufficient
identity. Live resolution adds the current document generation, frame and media-element endpoint, all of which become
stale on navigation or reload. The first production candidate removes every old binding for that tab on browser
`loading`. A temporary grant never creates a successor. An exact-site grant may create one new Page Binding after
the replacement top-level HTTPS document reaches `complete` and proves that its origin still has permission; the new
opaque identity is only a catalog candidate and never satisfies, selects or repairs a lock on the old identity. If
the Extension cannot prove continuity after browser restart, the old target remains unavailable rather than being
recovered by URL similarity.

Routing modes keep their user meaning across providers:

- Session Lock captures one exact Page Binding; reload／navigation removes it and never adopts an automatically
  created trusted-site successor without an explicit new lock action.
- App Lock captures a Browser Application Scope (browser profile plus origin／installed Web App identity), then applies
  a deterministic candidate policy; multiple unresolved pages are an ambiguity, not permission to choose list order.
- Priority Rules persist a typed selector and display whether it is page-scoped or application-scoped. Rules for two
  pages in the same browser remain distinct.
- Windows Auto persists no Browser Target, but every observation and route decision still names the exact resolved
  page rather than only the browser executable.

The generic site implementation uses stable `HTMLMediaElement` primitives after explicit `activeTab` or per-site
permission. Rich site Adapters may add metadata or commands behind the same Browser Adapter interface. Unsupported,
DRM-only or ambiguous pages advertise no direct capability and retain the existing GSMTC path.

Target presentation reconciliation is exact and fail-open toward the established GSMTC fallback: a Browser target
suppresses only the one GSMTC target named by an authoritative correlation while both identities are present. No
correlation is inferred from Brave／Chrome executable identity, title, URL, origin similarity, tab order or track
metadata. Therefore installing an Extension does not hide unrelated or uncorrelated Brave GSMTC targets.

The first production candidate composes the existing GSMTC Adapter as the primary provider with an optional Browser
Adapter. The Browser Module owns protocol v2, authorization, profile／Page Binding／Endpoint state, event-driven
playback／timeline／rate snapshots and one-shot command correlation. Generic Toggle Play／Pause resolves the exact
Endpoint's live paused state into one explicit Play or Pause. The provider-neutral input coordinator checks the
captured target through the shared target catalog, so a supported Browser Toggle is consumed without a GSMTC-only
capability lookup. A minimal Native Host validates the fixed Extension launch origin and relays bounded frames over the
fixed current-user-only named pipe to the running desktop process; it exposes no TCP／HTTP listener. See
[ADR 0007](adr/0007-use-a-current-user-native-messaging-bridge.md).

Generic Endpoint capabilities are live observations rather than bind-time constants. Metadata, duration, buffering
and timeline events recompute bounded Seek availability; the exact target registry updates its command gate before
publishing the corresponding neutral snapshot. Stale Page Binding／document／Endpoint observations cannot alter that
gate. If a trusted completed page exists before the desktop process, a serialized Extension-owned availability
monitor revalidates exact HTTPS permission and current document generation before rebinding. Its in-memory backoff is
1／2／5／10 seconds and then at most once per 30 seconds, with `chrome.alarms` as the Manifest V3 wake-up fallback.
Success, permission loss or absence of eligible trusted pages cancels pending work; no media command is retried.

This composition enables only exact Browser Session Lock at runtime. It emits no inferred GSMTC correlation, so Brave
GSMTC targets remain visible unless a future provider supplies an authoritative exact link. Browser target loss keeps
the provider-qualified lock in Recovery／Unavailable; it never asks the GSMTC primary provider for a similar target.
Because the Browser lock is runtime-only, it is never serialized into the GSMTC `RuntimeStateDocument`; catalog
refreshes and routed commands preserve the live lock without attempting an invalid durable Session Lock. The JSON
runtime repository independently validates every document before writing as a second integrity guard.

Playback State Lock also consumes the neutral `RouterState.Targets` observation rather than reopening a GSMTC-only
seam. Its Armed Playback Target remains a `MediaTargetId`: GSMTC targets retain fingerprint-based successor recovery,
while a direct Browser target has no inferred successor and may resume only when the Router again resolves the same
provider-qualified identity. A Browser Paused observation uses the existing serialized, bounded Play-correction path;
target loss publishes Suspended and cannot redirect the correction to a replacement Page Binding or GSMTC competitor.

The Browser Adapter also attaches a provider-owned, presentation-only browser-family group hint to each Browser target.
The App presentation Module may place an exact ordinary-browser GSMTC application and those Browser pages inside one
expandable visual group, while installed PWA identities remain separate. This grouping is not correlation evidence:
children retain provider-qualified identities, commands still route through their original Adapter and an
uncorrelated GSMTC child remains visible. Group expansion and selection are separate operations, and the WPF surface
uses one bounded outer scrollbar instead of nested or independently growing target lists. The App selection Module
maps its single Lock Session interface to `LockSession` for a GSMTC child or `LockTarget` for an exact Browser child;
the View never owns separate provider-specific lock buttons. Browser authorization revocation remains an exact-target
operation supplied with the row identity rather than inferred from ambient selection. Projection builds an exact
ordinary-browser GSMTC presentation group before attaching a matching Browser-family child, preserving the existing
group key when authorization arrives. Nested child lists delegate wheel input to the one outer scrolling surface.

The Extension Popup reads current-document authorization through a narrow internal message contract. It supplies the
active tab ID obtained from `chrome.tabs.query`, then asks the top-level document's installed generic Endpoint. The
content boundary validates the Extension sender and projects only `authorized` plus the Binding scope from its live
Page Binding. Browser site permission and service-worker memory are deliberately not fallback state sources:
reload／navigation may retain permission after the document Binding is gone, while closing and reopening the Popup
must not discard a live Binding. The Popup begins in Checking state, reports a distinct trusted-site waiting state
when permission remains but no Endpoint answers, and reports Not authorized only when neither Binding nor site grant
exists. It exposes no URL, title, opaque Binding identity or media metadata. Static labels and status prose use
Chromium i18n with English fallback and a Traditional Chinese locale.
For a site-scoped Endpoint, Popup projection also revalidates the exact origin permission; a stale document listener
cannot remain Authorized after permission revocation. Exact-target revocation sends an `unbindGenericEndpoint`
message to the browser-owned document before removing the registry entry; a missing document cannot block removal.
Trusted-site automatic binding separates preparation from publication and revalidates its tab generation after every
asynchronous boundary, so a result completed after reload／close is discarded without replacing a newer same-tab
target. Popup failure prose maps internal transport codes to localized, actionable messages plus stable `ML-BR-*`
support codes; this is the Browser Integration subset of the broader Phase 17 presentation contract.

### Planned installed Browser Integration lifecycle

Phase 19 places installed-package ownership behind one composition-time Interface rather than spreading registry and
manifest logic across the installer, App startup and UI. One deep installation Module accepts an immutable package
descriptor plus Ensure or Remove Owned intent, then returns a structured ownership result. It hides installed-versus-
portable qualification, canonical path checks, atomic manifest replacement, current-user registry access, exact owner
comparison, repair／upgrade transitions and localized problem mapping.

```text
Setup／installed startup／uninstall cleanup
                    │ package descriptor + intent
                    ▼
      Browser Integration installation Module
          │ manifest／registry  │ structured result
          ▼                     ▼
 current-user Windows Adapter   composition／diagnostics
```

The installed layout contains one self-contained Host and version-matched Extension files beneath the exact package
root. Chromium starts the Host through the package-owned manifest; no user launches it. A portable copy cannot satisfy
the installed-package descriptor and therefore cannot repair or remove that registration. Foreign, development and
ambiguous registrations remain untouched. Core, Router and the Browser command Adapter receive no installer paths or
ownership rules. This architecture is planned by
[Phase 19](phase-19/installed-browser-integration-plan.md) and is not implemented by the planning change.

## 10. Composition

Application startup is the composition root. It creates adapters, repositories, router and ViewModels through
constructor injection, enforces single-instance behavior, and starts services in a defined order. Shutdown stops
input first, drains or cancels routing work, persists state, removes subscriptions and then exits the tray process.
The same root injects the Windows environment and desktop-support adapters into Settings; tests replace both through
their public seams without launching Explorer, a browser or the Clipboard.

The login-startup adapter monitors the current-user Run key with `RegNotifyChangeKeyValue`. Application owns that
stream and reconciles notifications through its serialized settings boundary: an enabled preference repairs a stale
or foreign command to the current executable, while a disabled preference does not delete a value owned by another
portable copy. Shutdown cancels and joins this monitor before disposing Application coordination resources.

An installer-only `--uninstall-cleanup` command is handled before single-instance, GSMTC, tray or input initialization.
It delegates to the Windows startup adapter, which removes the current-user Run value only when its complete quoted
command matches the executing installed path. Missing or portable-owned values are preserved. The command produces no
desktop UI and reports cleanup failure through its process exit code for the uninstaller.

## 11. Publication

The release candidate targets `win-x64`, self-contained, single-file publication. Single-file output can be larger
and may interact with native libraries or extraction behavior, so build success, cold start, tray resources,
settings paths and clean-machine execution are release gates rather than assumptions.

Phase 12B enables compression for managed assemblies inside the single-file bundle while retaining self-contained
runtime and native-library self extraction. This reduces the installed executable but can make the outer Inno Setup
container larger because its LZMA2 compressor receives already-compressed input. Release metadata records the
single-file-compression state; EXE, ZIP, Setup, extraction cache and startup remain separate gates.
