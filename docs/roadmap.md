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

Implementation note: Phase 3 persists runtime state for later recovery, but intentionally does not restore a prior
Session Lock. Crash recovery and suspend/resume manager reacquisition remain Phase 4 exit criteria.

## Phase 4 — Recovery hardening

Implement Session loss detection, matching, configurable timeout/fallback, crash recovery and suspend/resume
reacquisition.

Exit criteria:

- Browser refresh/restart and application exit/restart scenarios pass the supported test matrix.
- Ambiguous candidates do not silently bind to an unsafe target.
- Every Recovery outcome is observable in UI and logs.

## Phase 5 — Rules and post-MVP features

Add App Lock and priority rules after Session Lock is reliable. Then evaluate artwork, seek, volume, custom hotkeys
and optional browser integration as separately scoped work.

Exit criteria:

- Rule ordering and conflicts have deterministic behavior.
- Browser integration is optional and cannot weaken GSMTC-only operation.
- Version scope is reflected in product and testing documentation.

## Release candidate

Produce a `win-x64` self-contained single-file candidate only after the MVP success criteria pass. Validate it on a
clean supported Windows environment before describing the package as portable.
