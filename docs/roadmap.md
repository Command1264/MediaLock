# Media Lock Roadmap

Each phase ends only when its exit criteria are met. Later phases must not conceal a failed Phase 0 assumption.

## Phase 0 — Technical validation

Build a Console Prototype that enumerates GSMTC Sessions, observes changes, chooses one Session and routes supported
commands. Evaluate candidate physical-media-key backends on real hardware and prove capture, consumption and routing
without duplicate Windows behavior.

Exit criteria:

- Session enumeration and event refresh work for Brave/YouTube Music and at least one second named, independent
  GSMTC source; the evidence records the exact application and version. Spotify Desktop remains part of the MVP
  compatibility matrix, but is not a unique Phase 0 feasibility gate.
- Manual command dispatch targets the selected Session.
- At least one input backend has documented support boundaries and repeatable evidence for consume behavior.
- Ordinary-user privilege behavior is known.
- Suspend/resume and Session recreation limitations are recorded.
- Any failed foundational assumption updates product scope before Phase 1.

## Phase 1 — Core

Implement immutable Session snapshots, Media Command, Session Fingerprint, Routing Mode, Route Decision, router
state transitions, Recovery and Fallback Policy behind platform-independent interfaces.

Exit criteria:

- State transitions and route decisions have deterministic unit tests.
- Concurrent event ordering is serialized and cancellation behavior is tested.
- Settings/state schemas and failure handling are defined.
- Core has no WPF, WinRT or Win32 dependency.

## Phase 2 — WPF shell

Implement the main WPF experience with MVVM: current target, Session list, manual media controls, lock/unlock and
Windows Auto. Keep ViewModels limited to presentation state and commands.

Exit criteria:

- Critical UI flows work through application services.
- No GSMTC or input interception logic lives in Views or ViewModels.
- Accessibility names, keyboard navigation and empty/error states are covered.

## Phase 3 — Tray, settings and lifecycle

Add system tray behavior, close-to-tray, explicit exit, JSON persistence, structured logging, single-instance,
login startup and settings UI.

Exit criteria:

- Startup and shutdown ordering is repeatable without orphaned subscriptions.
- Corrupt settings produce actionable recovery and preserve recoverable data.
- Tray state reflects Windows Auto, Locked and Recovering.
- Startup integration is reversible and does not require elevation.

Implementation note: Phase 3 persists runtime state for later recovery. Phase 4 consumes that state only when the
configured default is Session Lock and the persisted target has one unambiguous acceptable successor.

## Phase 4 — Recovery hardening

Implement Session loss detection, matching, configurable timeout/fallback, crash recovery and suspend/resume
reacquisition.

Implementation note: suspend releases the old manager and subscriptions. Resume publishes Reacquiring, makes at
most three bounded acquisition attempts, and remains observably Unavailable if all attempts fail; a later resume
can retry without restarting the catalog stream.

Exit criteria:

- Browser refresh/restart and application exit/restart scenarios pass the supported test matrix.
- Ambiguous candidates do not silently bind to an unsafe target.
- Every Recovery outcome is observable in UI and logs.

## Phase 5 — Rules and post-MVP features

Add App Lock and priority rules after Session Lock is reliable. Then evaluate artwork, seek, volume, custom hotkeys
and optional browser integration as separately scoped work.

Implementation note: App Lock is delivered before Priority Rules. It persists source application identity, reuses
one deterministic candidate policy for interactive and startup resolution, and remains distinct from Session Lock.
Priority Rules use an ordered, enabled list of exact source application identities. The first available match wins,
same-application selection reuses App Lock policy, and no match falls back to Windows Current Session. Every optional
media/browser feature uses a later independent branch.

Exit criteria:

- Rule ordering and conflicts have deterministic behavior.
- Browser integration is optional and cannot weaken GSMTC-only operation.
- Version scope is reflected in product and testing documentation.

## Phase 6 — Release candidate

Produce a `win-x64` self-contained single-file candidate only after the MVP success criteria pass. Validate it on a
clean supported Windows environment before describing the package as portable.

Exit criteria:

- One reviewed local command produces a versioned ZIP, manifest and SHA-256 checksum from a clean Git commit.
- The ZIP contains exactly one self-contained `MediaLock.exe`; trimming remains disabled unless separately proven
  safe for WPF and the Windows adapters.
- Release metadata records the source commit, .NET SDK, RID, version, signing state and archive digest.
- Cold start, tray resources, Settings, single-instance activation, startup registration, explicit Exit and user-file
  writes pass from the packaged executable.
- A clean supported Windows environment passes the documented smoke test before the artifact is called portable.
- Unsigned candidates remain clearly labeled; tag, GitHub Release and public distribution require separate approval.

Status: complete on 2026-08-23 for the formal `0.2.0-rc.1` candidate from commit
`a2e85007ec570344ab91518f0b1de918605be8a0`. Windows Sandbox independently verified its archive digest, cold start,
single-instance behavior, Settings and current-user files, reversible login startup, tray lifecycle, explicit Exit,
`MSEdge` Session discovery and routed Play/Pause. The candidate remains unsigned; no tag, GitHub Release or public
artifact is implied by phase completion.

## Phase 7 — UX and localization foundation

### Phase 7A — Localization foundation

Move presentation-owned text behind one App-layer localization module, persist a UI-language preference and ship
English plus Traditional Chinese resources. Keep routing vocabulary and state semantics unchanged.

Exit criteria:

- Settings offers Windows language, language-native `English` and `繁體中文` choices, applies a successfully saved
  change immediately and leaves the current culture unchanged when save fails.
- Main window, Settings, ViewModel projections, accessibility names and notification-area commands resolve through
  localized resources.
- Settings schema migration preserves existing v1-v3 user choices and defaults language to Windows language.
- Culture resolution, resource fallback, persistence and ViewModel language choices have automated coverage.
- Both languages and Windows-language restoration pass immediate-save and restart smoke tests without routing or
  lifecycle regressions.

### Phase 7B — WPF visual refresh and motion

Apply a coherent Windows 11-inspired WPF visual system, theme support, clearer state hierarchy and restrained motion
without changing the established Core/Application seams.

Exit criteria:

- Main and Settings share semantic Light and Dark palettes plus consistent cards, controls, typography and focus states.
- Settings is a fixed-size, rounded, frameless modal surface; its owner stays disabled, Cancel/Escape discard unsaved
  edits and closing returns directly to the main window after application switching.
- Settings offers Windows theme, Light and Dark, applies a successful save immediately and preserves the current theme
  when save fails; schema v1-v4 migration preserves existing settings.
- Windows-theme preference follows the current Windows app theme without restarting routing, GSMTC discovery or input.
- The Windows-owned main caption follows the resolved Light or Dark theme on supported Windows 11 builds.
- Routing status, current target, Session selection and primary media controls retain keyboard and accessibility behavior
  at the supported minimum window sizes in English and Traditional Chinese.
- Window motion is restrained and disabled when Windows client-area animation is disabled.
- Automated theme/settings/cancel/window-contract coverage and a repeatable Light/Dark desktop smoke test pass without
  routing regressions.

### Phase 7C — Now Playing artwork and timeline

Evaluate artwork and timeline presentation first; seek remains separately gated on real GSMTC capability evidence.

Exit criteria:

- The resolved routing target, rather than the merely selected Session, owns the displayed artwork and timeline.
- JPEG and PNG artwork is size-bounded, cached between metadata changes and treated as optional presentation data;
  missing, malformed or unreadable artwork cannot interrupt catalog refresh, Recovery or command routing.
- A valid timeline displays elapsed and total time. Playing position advances from the immutable GSMTC observation,
  while paused, stopped, missing and invalid timelines remain stable or hidden and every value is clamped to bounds.
- Target change, Session recreation and target loss cannot retain stale artwork or timeline state.
- English and Traditional Chinese, Light and Dark, supported minimum size and physical-media-key routing pass the
  focused desktop smoke test.
- Seek is not exposed until Brave YouTube and YouTube Music provide separate real-session capability and acceptance
  evidence.

## Phase 8 — Parameterized media controls

### Phase 8A — Seek capability probe

Extend only the disposable Console Probe to measure GSMTC playback-position support before changing the production
command model or making the timeline interactive.

Exit criteria:

- Session output records `IsPlaybackPositionEnabled` alongside the existing command capabilities.
- `seek <seconds>` accepts one finite, non-negative invariant-culture value, converts it to `TimeSpan` ticks and
  rejects requests outside the selected Session's current timeline without invoking GSMTC.
- The Probe records advertised capability, accepted/rejected result, requested position, prior observation and the
  immediate post-request observation without treating `accepted` as proof that playback actually moved.
- Brave YouTube Music and ordinary Brave YouTube are tested separately while both Sessions exist, in Playing and
  Paused states, with at least two in-range positions and one invalid/out-of-range request.
- Session recreation and competing-source isolation are recorded. The production UI remains read-only and Core's
  parameterless `MediaCommand` model remains unchanged.
- Evidence yields an explicit proceed/limit/reject decision for a separately scoped Phase 8B seek implementation.

### Phase 8B — Routed Seek and interactive timeline

Promote absolute Seek into the production Media Command model and make the routed target's timeline interactive while
preserving the existing Router, Recovery and GSMTC seams.

Status: complete. Routed Seek and the interactive timeline passed their automated and named Brave YouTube Music plus
ordinary Brave YouTube matrix; Phase 8C subsequently completed the physical-media-key regression row.

Exit criteria:

- One immutable Media Command value represents both transport actions and an invariant absolute Seek position; Seek
  uses the same Application and Router dispatch interfaces as every other command.
- The Router resolves exactly one target through the active Routing Mode, requires advertised Seek capability and a
  valid current timeline, and rejects out-of-range positions before calling the controller.
- The Windows adapter maps `IsPlaybackPositionEnabled`, converts the validated absolute position to GSMTC ticks and
  reports accepted, rejected or failed without treating acceptance as observed movement.
- The routed target timeline becomes an accessible Light/Dark Slider. Mouse, touch and keyboard interaction preview
  locally and commit exactly once per completed gesture; unsupported, Recovering and Unavailable targets remain
  non-interactive.
- Accepted Seek retains its preview until a later timeline snapshot confirms it. Target changes, rejection, failure or
  a bounded confirmation timeout restore the authoritative observed position and remain actionable.
- Brave YouTube Music and ordinary Brave YouTube pass Playing, Paused, competing-source, Session recreation,
  English/Traditional Chinese, minimum-size and physical-media-key regression checks.

### Phase 8C — Production global media-key interception

Promote the Phase 0 low-level keyboard backend into the desktop application and route accepted physical media keys
through the existing Application and Router boundaries.

Status: complete. The production backend, automated coverage, code review and ASUS ROG STRIX FLARE hardware-assisted
matrix were integrated on 2026-08-24.

Exit criteria:

- Play/Pause, Previous, Next and Stop are captured without elevation and routed once to the resolved target.
- Accepted KeyDown repeats and the matching Key-up are consumed consistently; unsupported, disabled, unavailable or
  backpressured input passes through to Windows.
- Capture-time target identity prevents a queued command from being redirected after a catalog or routing change.
- Settings schema v6 persists an enabled-by-default interception switch that takes effect immediately.
- Startup/runtime hook failures are observable and safely degrade to Windows media-key handling.
- The ASUS ROG STRIX FLARE matrix passes with Brave YouTube Music as Priority Target and ordinary Brave YouTube as
  Windows Current Session, including focus changes, long press, lock/unlock and sleep/resume.

## Phase 9 — `0.2.0-rc.2` release hardening

Produce a second reviewed `win-x64` candidate that consolidates the completed post-`rc.1` UX, localization, Now
Playing, Seek and physical-media-key work. Preserve the Phase 6 provenance boundary: a candidate is identified by its
exact source commit and archive digest, and evidence from `rc.1` does not transfer to `rc.2`.

Exit criteria:

- Project defaults, packaging tests, user documentation and release notes consistently identify `0.2.0-rc.2`.
- Restore, formatting, automated tests, Release build and isolated packaging verification pass locally without relying
  on GitHub Actions capacity.
- A clean reviewed commit produces the ZIP, manifest and SHA-256 set; the archive contains exactly one correctly
  versioned self-contained `MediaLock.exe`, and the independently computed digest matches the manifest.
- The packaged executable passes host smoke coverage for startup, single-instance activation, Settings, routing,
  physical-media-key interception, tray lifecycle and explicit Exit.
- Windows Sandbox repeats cold-start, persistence, startup-registration, Edge GSMTC routing and explicit-Exit checks
  for the exact `rc.2` source commit and digest.
- The candidate remains explicitly unsigned. Tagging, GitHub Release creation and public artifact publication remain
  separate operations requiring explicit approval.

Status: in progress. Automated gates, clean artifact inspection and local-host smoke passed for source commit
`aca17b40f3b6300ca4e2eeeca2590dfbbf7287a7`; the Windows Sandbox gate remains pending.
