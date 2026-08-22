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

- Settings offers Windows language, English (`en-US`) and Traditional Chinese (`zh-TW`) and states when the change
  takes effect.
- Main window, Settings, ViewModel projections, accessibility names and notification-area commands resolve through
  localized resources.
- Settings schema migration preserves existing v1-v3 user choices and defaults language to Windows language.
- Culture resolution, resource fallback, persistence and ViewModel language choices have automated coverage.
- Both languages pass a restart-based desktop smoke test without routing or lifecycle regressions.

### Phase 7B — WPF visual refresh and motion

Apply a coherent Windows 11-inspired WPF visual system, theme support, clearer state hierarchy and restrained motion
without changing the established Core/Application seams.

### Phase 7C — Now Playing artwork and timeline

Evaluate artwork and timeline presentation first; seek remains separately gated on real GSMTC capability evidence.
