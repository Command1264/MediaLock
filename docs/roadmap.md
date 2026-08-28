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

Status: complete on 2026-08-24. Automated gates, clean artifact inspection, local-host smoke and Windows Sandbox passed
for source commit `aca17b40f3b6300ca4e2eeeca2590dfbbf7287a7` and archive SHA-256
`0c750e7f2eec132b6b82c4d78f491f961dad76358c4e0b9c49dc3042779ec5e7`. The candidate remains unsigned; no tag,
GitHub Release or public artifact is implied by phase completion. A separately approved publication operation later
created GPG-signed annotated tag `v0.2.0-rc.2` at the candidate source commit and a public GitHub Prerelease containing
only the verified ZIP asset; the executable remains unsigned.

## Phase 10 — Public feedback and stable-release readiness

### Phase 10A — Feedback foundation

Give prerelease users one trustworthy path from download through verification, update, rollback, removal,
troubleshooting and structured feedback. Keep support evidence privacy-conscious and compatible with the existing
GitHub Issues triage roles.

Exit criteria:

- README links directly to the current official GitHub Release, its SHA-256, installation guidance and support entry
  points without describing the shipped application as merely planned.
- User documentation covers archive verification, first run, the exact-path effect of login startup, side-by-side
  update, rollback, portable removal and optional user-data cleanup.
- Bug and Compatibility Issue Forms collect the exact Media Lock version, Windows build, source application,
  Routing Mode, reproduction/result data, competing-source behavior and only sanitized diagnostic evidence.
- Blank issues are disabled; every new report starts in `needs-triage`, with `needs-info`, `ready-for-agent`,
  `ready-for-human` and `wontfix` available for later classification.
- Troubleshooting distinguishes Media Lock defects from missing or non-cooperating GSMTC capability and documents
  safe handling of unsigned downloads and `%LocalAppData%\MediaLock\` data.
- Relative documentation links and Issue Form YAML pass local validation; the full diff passes Standards and Spec
  review without relying on GitHub Actions capacity.

Stable-release decision gate:

- Use `0.2.0-rc.3` when any product behavior, packaging contract or persisted behavior changes after the tested
  `rc.2` artifact and therefore needs another prerelease validation cycle. A new candidate receives its own clean
  commit, digest, host and Windows Sandbox evidence.
- A documentation- or repository-metadata-only correction does not mutate the existing `rc.2` artifact, but public
  instructions must still identify that artifact exactly.
- A stable `0.2.0` build always receives its own versioned clean commit, archive digest, host evidence and Windows
  Sandbox evidence; RC evidence does not transfer to the differently versioned stable executable.
- Publish stable only when the chosen source has no unresolved Critical or High defect, every claimed
  compatibility row has named evidence, download/hash/install/update/rollback/remove paths are verified, unsigned
  status remains explicit, and all relevant automated and clean-environment gates pass.
- Tagging, stable GitHub Release creation and public artifact publication remain separately approved remote operations.

Status: complete on 2026-08-24. The four missing canonical triage labels were created, PR #20 integrated the reviewed
foundation into `develop`, and PR #21 synchronized it to default branch `main`. Authenticated public-surface validation
confirmed both forms render their required environment, routing and privacy fields, apply the intended labels, link to
Support, and hide blank issues from non-maintainers. No tag, Release or artifact changed.

### Phase 10B — In-app About and privacy-safe diagnostics

Let a prerelease user identify the running build and collect the minimum useful troubleshooting facts without finding
files manually or pasting private media metadata.

Exit criteria:

- Settings contains one bilingual, themed About and diagnostics card within its existing scrolling modal surface.
- Version, Windows product/display/build/architecture, prerelease/stable state and embedded-signature state are visible.
- Copy diagnostics produces native-line-ending text from an explicit allowlist: environment, Routing Mode/status,
  catalog status, actual media-key interception availability, Session count, Recovery timeout and Fallback Policy.
- The standard summary omits media title, artist, account name, full path, persisted target identity and complete settings.
- Open logs creates and opens the bounded log directory; Open support and Report a bug use the canonical GitHub pages.
- External desktop actions are replaceable in tests and failures remain visible as localized actionable errors.
- Successful copy confirmation follows live language changes and clears after five seconds or when Settings closes.
- Light/Dark, English/Traditional Chinese, keyboard focus, automation names and narrow wrapped button layout pass the
  documented manual and automated checks.
- Repository Topics describe the actual Windows/.NET/WPF/GSMTC/media-routing scope without exceeding GitHub limits.
- Because executable behavior changes after `rc.2`, completion feeds an independently built and validated
  `0.2.0-rc.3`; no `rc.2` artifact evidence transfers.

Status: complete on 2026-08-24. Repository Topics were updated and verified, both review axes finished with zero
findings, and PR #24 integrated the implementation into `develop`. All 280 automated tests, the isolated packaging
contract, and the bilingual Light/Dark, support-action, keyboard-focus and physical-media-key host matrix passed.
This completes the Phase 10B product work only: a formal `0.2.0-rc.3` artifact still requires its own clean build,
digest, host validation, Windows Sandbox validation and separately approved publication operation.

### Phase 10C — `0.2.0-rc.3` release validation

Package the Phase 10B executable changes as a third unsigned `win-x64` prerelease candidate without transferring any
artifact identity or runtime evidence from `0.2.0-rc.2`.

Exit criteria:

- Release notes, runbook, project metadata and packaging tests consistently identify `0.2.0-rc.3` while public
  download guidance continues to identify `0.2.0-rc.2` until a separately approved publication occurs.
- Restore, formatting, 280-or-more automated tests, Release build, isolated packaging verification and two-axis review
  pass locally without GitHub Actions capacity.
- A clean reviewed commit produces exactly one self-contained `MediaLock.exe` in the ZIP plus a manifest and checksum
  whose independently recomputed SHA-256 values agree.
- Local-host validation covers version/signature facts, About and diagnostics actions, privacy-safe clipboard output,
  Settings persistence, routing, media-key isolation, single instance, tray lifecycle and explicit Exit.
- Windows Sandbox independently verifies the exact commit and digest, cold start without a separately installed .NET
  runtime, About/diagnostics, current-user startup registration, Edge GSMTC routing and clean explicit Exit.
- Tagging, GitHub Prerelease creation and public artifact upload remain separate remote operations requiring approval.

Status: complete on 2026-08-24 for source commit `10dbb5b1452fe27084a28e254388fe974ed277e6` and archive
SHA-256 `ee7e2174e54177c77d9edbe1233e94ed79f3613b42b782d3319c1357affa0f8a`. All automated, isolated
packaging, exact-artifact local-host and Windows Sandbox gates passed. The candidate remains unsigned; no tag, GitHub
Prerelease or public artifact publication is implied by phase completion. A separately approved publication operation
later merged PR #26 into `develop`, synchronized PR #27 to `main`, created GPG-signed annotated tag `v0.2.0-rc.3` at
the candidate source commit and published the verified ZIP as the sole asset of the public GitHub Prerelease.

### Phase 10D — `0.2.0` stable release

Promote the final validated product behavior to stable `0.2.0` without adding a feature after `0.2.0-rc.3`. The stable
executable receives an independent identity and complete release evidence.

Status: complete and published on 2026-08-24 for source commit
`7ce40ab31433998665b30ac18a7f50ebb3dafec7` and archive SHA-256
`f368421481fa0a99516618873dfd4e0422c241deae2033b105869471eab27bb0`. Automated gates, two-axis review,
clean artifact inspection, local-host smoke and Windows Sandbox passed. After separate publication approval, PR #31
merged the release branch into `develop`, PR #32 synchronized `main`, GPG-signed annotated tag `v0.2.0` identified
the exact artifact source commit, and the verified ZIP became the sole asset of the Stable／Latest GitHub Release.
The release remains unsigned and the long-lived `release/0.2` branch remains the stable hotfix baseline.

Exit criteria:

- Project defaults, packaging tests, user documentation and release notes identify stable `0.2.0`; historical RC
  records remain unchanged.
- The publishing command accepts both stable semantic versions and the existing `-rc.N` form, rejects dirty formal
  source and produces the ZIP/manifest/checksum provenance set.
- Restore, formatting, complete automated tests, Release build, isolated packaging and two-axis review pass locally
  without relying on GitHub Actions capacity.
- A clean reviewed commit produces exactly one self-contained `MediaLock.exe` whose ProductVersion is `0.2.0`,
  FileVersion is `0.2.0.0`, Authenticode is `NotSigned`, and independently recomputed hashes agree.
- Local-host and Windows Sandbox gates independently pass for the exact stable source commit and digest; RC results do
  not transfer.
- The long-lived `release/0.2` branch remains available as the stable hotfix baseline after integration and publication.
- Tagging, stable GitHub Release creation, Latest designation and public ZIP upload remain separately approved remote
  operations.

## Phase 11 — Playback intent and Windows media surface

Phase 11 targets `0.3.0`. It does not mutate the published stable `0.2.0` release or its retained hotfix baseline.

### Phase 11A — Playback State Lock

Let the user explicitly choose Off or one-way Keep Playing for the current routed target without turning ordinary
Play/Pause controls into an unbounded automation loop or adding a Keep Paused mode.

Status: complete. PR #35 integrated the one-way Keep Playing policy, repeated-pause escape hatch and named host
validation into `develop` on 2026-08-25.

Exit criteria:

- Core defines Off/Keep Playing eligibility and deterministic correction decisions without WPF or Windows dependencies.
- Application arms the policy to a captured target, serializes observations and corrections, and sends only explicit
  Play with `ExpectedTarget`; Pause and TogglePlayPause are never used for enforcement.
- Media Lock Pause/TogglePlayPause/Stop clears the policy before routing; Play/Next/Previous preserves it.
- Windows lock-screen Pause/Stop clears the policy; lock/unlock without a playback change preserves it, using a fresh
  post-unlock GSMTC observation rather than guessing from stale state.
- Recovery may resume only for the Router-accepted successor. Fallback, unrelated target changes, ambiguity, catalog
  loss, suspend and shutdown cannot redirect an enforcement command.
- Correction confirmation is observation-driven and bounded to two unconfirmed attempts; exhaustion is visible and
  deterministically tested. Keep Playing does not restart Stopped, Closed or naturally exhausted playback.
- An enabled-by-default repeated-pause escape hatch turns Keep Playing Off on the third distinct direct
  Playing-to-Paused transition within five seconds, leaves the third request paused and optionally plays one system
  sound. Settings constrain the window to 1–60 seconds and the threshold to 2–10; schema v7 migrates v1–v6 defaults.
  Changing/buffering, duplicate Paused, Recovery, target/lifecycle changes and Media Lock commands do not count.
- The current-target UI exposes Off and the active locked state in English/Traditional Chinese and Light/Dark without
  resizing controls. The first version is not restored on process startup.
- Real YouTube Music plus ordinary YouTube evidence covers external state changes, Next/Previous, Recovery, competing
  sources, lock/unlock, sleep/resume, physical media keys and explicit Exit with no duplicate or competing action.

### Phase 11B — Windows Media Surface Mirror probe

Measure whether a Media Lock-owned SMTC Session can make Windows' native media surface usefully reflect and control the
routed target. This is a feasibility probe, not a production promise.

Status: complete on 2026-08-25 with a final **Limit** decision. The documented mirror synchronized target data and
routed native-surface actions exactly once, but Windows did not reliably retain it as Current Session after control,
unlock or sleep/resume. See [`phase-11/windows-media-surface-probe.md`](phase-11/windows-media-surface-probe.md).

Exit criteria:

- The probe uses documented desktop SMTC interop and records Windows build, current-session identity, rendered metadata,
  controls, timeline, button/seek events, Recovery and lifecycle outcomes separately.
- Media Lock's own Session is excluded from the catalog and can never be selected as a routing target or create a
  command feedback loop.
- Each system-surface action enters the existing Application/Router path once and retains capture-time target identity.
- The probe compares YouTube Music with a competing ordinary YouTube Session across target changes, reload, lock/unlock,
  sleep/resume and process shutdown on named supported Windows builds.
- Evidence ends in an explicit proceed, limit or reject decision. It does not infer that Media Lock controls Windows
  Current Session merely because the mirror Session exists.

### Phase 11C — Production mirror integration (conditional)

Proceed only if Phase 11B demonstrates reliable, supportable native-surface behavior. Production work must add a
replaceable adapter, fake-driven Application coverage, self-session exclusion, accessibility/localization, stale-state
cleanup and a documented compatibility boundary. If the probe fails, record the limitation and scope any Media Lock
on-screen display as a separate feature rather than calling it native Windows synchronization.

Status: not proceeding. Phase 11B could not establish the required Current Session persistence, so Media Lock does
not ship or promise a native Windows media-surface replacement. A separately scoped best-effort mirror or Media Lock
on-screen display would require a new product decision.

## Phase 12 — Distribution and footprint

Phase 12 is planned after Phase 11 and is not part of Playback State Lock.

### Phase 12A — Installable Windows package

Provide an ordinary-user installation path that registers Media Lock in the Start menu so Windows Search can find it,
supports upgrade and uninstall, and keeps login-startup paths valid. Compare MSIX with an installer-based package before
selecting a format; code-signing and SmartScreen behavior must be stated precisely rather than implied by packaging.
The portable ZIP remains available until an installed migration and rollback path has passed a clean-Windows gate.

Status: complete on 2026-08-25. PR #38 integrated a per-user Inno Setup EXE at a stable
`%LocalAppData%\Programs\MediaLock\` path that the release command produces beside the existing portable ZIP. The
first RED → GREEN slices
produce ZIP and Setup from one payload and protect startup cleanup from deleting a portable-owned Run value. Local
silent install/uninstall has verified current-user registration, Start Menu discovery, matching startup cleanup and
default user-data retention. A clean Windows Sandbox transaction gate also passed for commit
`6233da8bab35e6fcde0858d1fa0a58fe5babfba6`, including payload/digest matching, default-disabled startup, owned versus
portable startup cleanup and retained user data. The same artifact's visible Sandbox smoke passed the
ordinary-user wizard without UAC, fixed destination, cold launch without a separate .NET prompt, Settings, search-panel
shortcut launch, single-instance restore and Tray restore. Indexed keyword search remains unverified because Sandbox
disabled its search index. The subsequent host/manual gate passed Windows Search, single-instance and Tray restore,
actual login startup, Play/Pause, Next/Previous, Recovery, competing-source isolation, uninstall and retained user
data without a reported error or crash. Clean source commit
`ed05c2742bdc6f3b0d5760406c6c3c410533ff9d` then produced test-only `0.2.0` and `0.2.1` artifacts whose hashes and
Sandbox matrix passed in-place upgrade with one Installed apps entry, retained user data and an unchanged startup
command. Older installers are now blocked with exit code 7 and an actionable message; a Ready-page cancellation
returned exit code 2 and left the old installation unchanged. Cancellation during extraction is not claimed because
the payload completed before the cancel action arrived. MSIX is deferred while
direct public installation requires a trusted signature and packaged-startup migration; MSI/WiX is deferred until
enterprise deployment or repair becomes a concrete requirement. See the
[Phase 12A plan](phase-12/installable-package-plan.md),
[packaging ADR](adr/0004-use-inno-setup-for-first-installer.md) and
[official-source comparison](research/windows-installation-packaging-options.md).

### Phase 12B — Footprint measurement and optimization

Measure the compressed archive, single-file executable, managed framework, native WPF/WinRT payload and runtime
extraction separately before choosing an optimization. Research an optional framework-dependent package and compare
safe publish settings; a complete framework-dependent footprint remains deferred until it includes the separately
required Desktop Runtime. Trimming, native-library exclusion or compression changes do
not ship unless WPF resources, GSMTC, tray, localization, startup and clean-machine tests pass; reducing bytes must not
silently remove the current no-runtime-install promise from the portable package.

Status: complete on 2026-08-25. The repeatable host benchmark compares the current payload with
single-file compression and optional supported-locale candidates while preserving self-contained, single-file,
native-self-extract, trimming-off and ReadyToRun-off constraints. On the i7-8700 reference host, the first compression
run reduced the installed／portable EXE by 58.91% with final 15-sample fresh- and warm-cache median startup regressions
of 2.86% and 2.04%, but increased the Inno Setup download by 37.24%. The accepted candidate enables single-file
compression while retaining all language resources; the manifest records this explicitly. Supported-locale filtering
reduced EXE, ZIP and Setup by 9.11%, 6.98% and 3.46%, but remains test-only rather than shipping in this phase. See the
[Phase 12B plan](phase-12/footprint-optimization-plan.md) and
[official-source research](research/dotnet-wpf-publish-footprint.md). The exact clean-commit i7-8700 evidence and raw
samples are preserved in the [host footprint benchmark](phase-12/host-footprint-benchmark.md). The compressed
candidate subsequently passed host routing, Recovery, lock／unlock, sleep／wake, Tray and localization smoke. A fresh
Windows Sandbox independently passed artifact identity, clean install, visible launch, single-instance, uninstall,
owned-startup cleanup and user-data retention checks.
The product owner explicitly waived a second reboot solely for a direct A/B pair after accepting the 15 + 15 sample
result and ordinary candidate startup smoke. Full Phase 12A upgrade／downgrade／cancellation and login-startup
transactions were inherited because their owning installer and startup code did not change; the exact compressed
payload repeated clean install／launch／uninstall and critical runtime routing instead.

## Phase 13 — `0.3.0` release preparation

Phase 13 turns the completed Phase 11A, 12A and 12B work into a reviewable `0.3.0-rc.1` candidate. It does not reopen
the Phase 11B Limit decision, add a best-effort Windows media-surface mirror or mutate the published `0.2.0` release.
The retained `release/0.2` branch remains the current stable hotfix baseline until a later verified `release/0.3`
stable baseline exists.

### Phase 13A — `0.3.0-rc.1` scope and gate definition

Freeze the candidate contents, compatibility boundary, artifact policy and repeatable host／Windows Sandbox evidence
before changing product version metadata or producing a formal candidate.

Status: complete. PR #40 integrated the scope and gate definition into `develop` on 2026-08-25. See the
[Phase 13 release-candidate plan](phase-13/release-candidate-plan.md).

Exit criteria:

- Candidate scope contains the completed one-way Keep Playing feature, per-user Inno Setup installer and accepted
  single-file compression profile, without adding another product feature.
- The documented Phase 11B Limit remains explicit: `0.3.0-rc.1` does not promise to own or remain first on Windows'
  native media surface.
- Upgrade coverage is defined from public portable `0.2.0` and, separately, from a clean per-user installation to the
  candidate installer. The existing two-stable-version Sandbox helper must be extended before it is used with an RC.
- README, installation guidance, release runbook, release notes and testing evidence are all named deliverables of
  candidate execution rather than being updated piecemeal after publication.
- Public candidate assets are limited to portable ZIP and Setup EXE. Their SHA-256 values appear in the Release body;
  manifest and standalone checksum files remain local provenance evidence unless a later publication decision changes
  that policy.
- ZIP, Setup and contained executable remain explicitly unsigned. No package format is presented as suppressing
  SmartScreen, Smart App Control or reputation warnings.
- Local gates are authoritative because GitHub Actions capacity is unavailable. Tagging, GitHub Prerelease creation
  and public artifact upload remain separately approved remote operations.

### Phase 13B — `0.3.0-rc.1` implementation and validation

After approval, change version and candidate documentation on one task branch, extend prerelease upgrade automation,
run the complete automated gate, then build one provenance-clean ZIP／Setup pair from the reviewed exact commit. Repeat
the named host and clean Windows Sandbox matrices and preserve exact digests and outcomes before review or publication.

No `release/0.3` branch is created for the prerelease. Create and retain that long-lived hotfix baseline only when a
verified `0.3.0` stable release is ready. Push, PR, merge, tag, GitHub Prerelease and public upload follow their normal
separate authorization boundaries.

Status: complete. Product metadata targets `0.3.0-rc.1`／`0.3.0.0`; the PowerShell 5.1-compatible installer gate selects
explicitly named stable／prerelease artifacts without `[version]`. The complete 351-test gate, review, exact-artifact
host smoke and Windows Sandbox matrix passed. Clean source commit
`d0fe5583e91204fe98a79b14ae0327e5120af54e` produced the independently matching ZIP／Setup／payload identities recorded
in the [Phase 13B packaged validation](phase-13/release-candidate-smoke.md).

The separately approved publication created GPG-signed annotated tag `v0.3.0-rc.1` at that source commit and a
[public GitHub Prerelease](https://github.com/Command1264/MediaLock/releases/tag/v0.3.0-rc.1) on 2026-08-26. Its only
assets are the verified ZIP and Setup; `v0.2.0` remains Stable／Latest and `release/0.2` remains the stable hotfix
baseline.

## Phase 14 — `0.3.0` stable promotion

Phase 14 promotes the frozen `0.3.0-rc.1` feature set to stable `0.3.0`. It adds no product feature, does not reopen the
Phase 11B Limit decision and does not mutate any published `v0.2.0` or `v0.3.0-rc.1` asset. See the
[Phase 14 stable-release plan](phase-14/stable-release-plan.md).

### Phase 14A — stable scope and gate definition

Freeze stable identity, RC1-to-stable transition coverage, exact-artifact host／Sandbox evidence, release-branch
retention and publication boundaries before changing version metadata.

Status: complete. PR #44 integrated the reviewed scope and gate definition into `develop` on 2026-08-26.

Exit criteria:

- Stable scope exactly matches `0.3.0-rc.1`; a blocker fix requires separate approval and regression coverage, while a
  material behavior change requires another RC.
- Stable ProductVersion is `0.3.0`, FileVersion remains `0.3.0.0`, and historical candidate evidence stays immutable.
- Windows PowerShell 5.1 and PowerShell 7 transition coverage includes public RC1 Setup to stable, stable repair,
  stable-to-RC1 downgrade protection and Ready-page cancellation.
- Formal stable ZIP and Setup share one reviewed payload and receive independent clean-commit provenance, host and
  Windows Sandbox evidence; no RC result transfers automatically.
- `release/0.3` is created and retained only after the exact stable source/artifacts pass, while `release/0.2` and its
  Worktree remain untouched during Phase 14.
- Push, PR, merge, `main` synchronization, branch creation, tag, GitHub stable Release, Latest designation and public
  uploads retain their documented authorization boundaries.

### Phase 14B — stable implementation and validation

After separate approval, change only stable identity, transition automation and release documentation, run the complete
local gate and two-axis review, then create and validate one provenance-clean `0.3.0` ZIP／Setup pair on the host and a
fresh Windows Sandbox. Record results in `docs/phase-14/stable-release-smoke.md` without rebuilding the artifact.

Status: complete on 2026-08-26. All 365 automated tests, Release build, formatting and isolated packaging contracts
passed. Exact-artifact host and fresh Windows Sandbox validation passed for source
`a773fac983728f5d4b2d8cbe40bfad9d1c016737`; the ZIP SHA-256 is
`94deac66e195cfca21c826ee86f3e61097f3440d3a84ebb5af2439ef3ad3d437` and the Setup SHA-256 is
`7937f807b2ec577b88d3506735dfb7c26fe0c12b0f055a2b739364bb9ab4d00d`. PR #46 integrated the reviewed result into
`develop`.

### Phase 14C — integration and stable publication

After Phase 14B passes and receives explicit remote-operation approval, establish the retained `release/0.3` baseline,
integrate the stable change through `develop` and `main`, then separately create the signed annotated `v0.3.0` tag and
GitHub Stable／Latest Release with only the verified ZIP and Setup. Preserve all older public Release assets and keep
`release/0.2` unless a later explicit maintenance decision authorizes otherwise.

Status: complete and published on 2026-08-26. PR #47 synchronized the stable implementation to `main`, retained
`release/0.3` was created at exact artifact source `a773fac983728f5d4b2d8cbe40bfad9d1c016737`. The separately approved
publication created GPG-signed annotated tag `v0.3.0` and a
[GitHub Stable／Latest Release](https://github.com/Command1264/MediaLock/releases/tag/v0.3.0) containing only the
independently verified ZIP and Setup. A later authorized maintenance step retired the local／remote `release/0.2`
branch and Worktree after confirming the new baseline and integration; historical `v0.2.0` tag, Release, asset and
provenance remain unchanged.

## Phase 15 — Human-readable source identities

Replace raw source-application identifiers in user-facing Session and Priority Rules surfaces with a trustworthy,
human-readable presentation while preserving the exact GSMTC identity used by routing. For example, an installed
YouTube Music PWA currently exposed as `Brave._crx_cinhimbnkkghhklpknlkffjgod` may display as
`YouTube Music — Brave` when Windows application metadata can verify both parts.

This is a presentation resolver, not browser URL detection and not an identity migration. App Lock, Priority Rules,
Recovery and persisted state continue comparing the complete `SourceAppUserModelId`; the friendly name may change
without changing the routed target. The resolver must prefer authoritative Windows application／package metadata,
retain the raw identifier in an accessible details surface, and fall back to that identifier when no reliable name
exists. It must not infer an application name from a song title, artist, browser tab title or hard-coded `_crx_` value.

Exit criteria:

- Ordinary browsers, installed Chromium PWAs, packaged apps and classic desktop sources have deterministic display
  resolution and raw-ID fallback behavior.
- Duplicate friendly names remain distinguishable without changing their routing identities.
- Main Session rows, Priority Rules, App Lock details, accessibility text and English／Traditional Chinese surfaces
  use one shared resolver rather than independent labels.
- PWA reinstall／identity changes and unavailable Windows metadata fail safely without rebinding a saved rule to a
  different source.
- Unit tests cover resolver precedence, collisions, fallback and localization; a named Brave YouTube Music PWA plus
  ordinary Brave YouTube smoke confirms that display improvements do not alter routed commands or Recovery.

Status: implementation and named desktop acceptance complete on 2026-08-26 after the `0.3.0` stable release. The
candidate uses exact Windows AppsFolder AUMID/display-name metadata with raw-ID fallback and does not alter routing
identity. All 383 automated tests, Release build, formatting, packaging regression and two-axis review passed. A named
Brave YouTube Music PWA／ordinary Brave YouTube smoke confirmed shared Main／Settings presentation, raw-ID details,
physical Play／Pause isolation and Session Lock Recovery without rebinding. It is not part of the current stable
artifact. See
[`phase-15/human-readable-source-identities.md`](phase-15/human-readable-source-identities.md).

## Phase 16 — Direct browser integration

Add an optional, support-bounded browser-control path for standards-based web media that can address an exact page or
installed PWA without using that source's GSMTC Session as the command transport. A generic Adapter covers compatible
`HTMLMediaElement` pages such as directly hosted／cloud MP4 and ordinary streaming media; site Adapters add richer
capabilities where a documented platform contract exists. The existing GSMTC adapter remains the universal fallback
for unsupported sites, browsers and desktop media applications. This phase does not promise to replace Windows GSMTC,
suppress Sessions owned by other processes or force Media Lock to remain Windows Current Session.

The intended shape is a peer browser adapter behind a neutral media-target seam, not browser-specific behavior inside
the existing GSMTC adapter:

```text
Authorized web media ──▶ browser extension ──▶ native messaging ──▶ browser adapter
GSMTC applications ──────────────────────────────────────────────▶ GSMTC adapter
                                                                       │
                                                                       ▼
                                                         existing routing semantics
                                                                       │
                                                                       ├─▶ physical media keys
                                                                       └─▶ optional Media Lock SMTC mirror
```

The work is divided into three independent evidence gates. Passing direct control does not imply that Chromium's own
SMTC publication was suppressed, and neither result implies that Windows will select the Media Lock mirror:

1. **Direct-control gate** — identify and control the intended YouTube／YouTube Music tab, observe metadata, playback
   state and timeline, and survive navigation, reload, browser restart and native-host reconnect without duplicate
   commands.
2. **Source-publication gate** — determine whether each supported browser can voluntarily disable its own Windows
   media integration through a documented, user-reversible mechanism. Chromium feature flags may be measured as an
   experimental controlled-browser option, but are not a stable cross-browser product contract.
3. **Windows-projection gate** — separately evaluate a best-effort Media Lock-owned SMTC mirror. Phase 11B's Limit
   decision remains authoritative: public Windows APIs cannot guarantee that the mirror is Current or the only card.

Exit criteria:

- A disposable extension/native-host prototype passes Chrome, ordinary Brave and installed Brave YouTube Music PWA
  tests before production architecture or packaging is approved.
- Play, Pause and Seek use stable web media primitives where available; site-specific Next／Previous behavior is
  isolated, version-detected and fails closed when the page contract changes.
- Browser messages validate extension origin, site origin, target identity, schema, size and a command allowlist;
  native-host access is limited to approved extension identities.
- Direct targets have explicit identity, capability and Recovery semantics that do not silently rebind existing
  GSMTC App Lock, Session Lock or Priority Rules.
- Session Lock and page-scoped Priority Rules retain an exact Page Binding. Browser App Lock uses an exact Browser
  Application Scope plus deterministic page selection; no mode treats a browser executable as the complete target or
  silently merges two pages into one rule.
- Extension absence, permission denial, unsupported pages, disconnects and adapter failures fall back safely to the
  established GSMTC-only operation without claiming that a command succeeded.
- A clean profile with no Media Lock Extension installed passes the complete `0.3.0` regression matrix without an
  installation prompt, disabled GSMTC feature or settings migration. Installing the Extension only adds finer browser
  identity and direct capabilities for explicitly supported targets.
- Tests distinguish provider absence from loss of an already-bound direct target: absence starts and remains
  GSMTC-only, while bound-target loss preserves identity and follows Recovery without rerouting to a competitor.
- No process injection, Windows component replacement, undocumented Current Session setter or system-wide Session
  suppression enters production scope.
- User-facing documentation distinguishes "Media Lock controls the selected browser target directly" from
  "Windows shows only Media Lock"; the latter is never promised without separate source-by-source evidence.

Status: Phase 16A Gate A completed and merged through PR #56. Phase 16B has a final disposable Probe candidate: an
explicit gesture creates a temporary or exact-site Page Binding for one unambiguous top-level media Endpoint, and
routes Play, Pause and bounded Seek through the existing Native Messaging protocol. The production candidate later
adds exact live-state Toggle Play／Pause for the UI and physical media-key path. Exact-site reload Recovery,
permission revocation, stale-target invalidation and fixed／generic Adapter coexistence have automated coverage. The
real-browser final Gate passed on Chrome with temporary／exact-site authorization, command isolation, permission
revocation, ambiguity, iframe fail-closed, lifecycle and no-Extension GSMTC compatibility evidence. A subsequent
Brave compatibility-closure Gate passed named direct MP4, cloud-hosted MP4, ordinary streaming, ambiguity,
unsupported-item／iframe fail-closed, lifecycle and installed-`0.3.0` no-Extension GSMTC rows. Nested-frame selection and
production Router／persistence／release integration remain out of this Probe, and the no-Extension GSMTC path stays
authoritative. See the
[`Phase 16A browser-direct Probe plan`](phase-16/browser-direct-probe-plan.md),
[`Phase 16A browser-direct Probe evidence`](phase-16/browser-direct-probe-evidence.md),
[`Phase 16B generic web media Adapter plan`](phase-16/generic-web-media-adapter-plan.md),
[`Native Messaging security boundary`](research/phase-16-native-messaging-security-boundary.md) and
[`direct-browser integration limits`](research/direct-browser-integration-and-gsmtc-limits.md).

### Phase 16C — production integration gate

Define and review the provider-neutral production seam, close or explicitly narrow the remaining generic-media
compatibility rows, and stage Session Lock before any Browser Direct code enters production routing, persistence, UI
or packaging.

Status: the gate definition was accepted through PR #59 and the provider-neutral Core／Application seam is tracked by
[GitHub Issue #60](https://github.com/Command1264/MediaLock/issues/60). The seam is integrated, and
[Issue #62](https://github.com/Command1264/MediaLock/issues/62) carries the first Browser Session Lock candidate:
exact runtime Page Binding, Play／Pause／Toggle Play／Pause／bounded Seek, explicit authorization／revoke UI, a current-user
Native Messaging bridge and an unpacked Extension. Other Routing Modes, persistence／schema migration, store
distribution, installed package ownership and release qualification remain later independent gates. See the
[candidate runbook](phase-16/browser-session-lock-candidate.md) and [ADR 0007](adr/0007-use-a-current-user-native-messaging-bridge.md).

## Phase 17 — Localized warnings and stable error codes

Replace user-facing raw warning and error strings with a structured presentation contract. Every semantic warning or
error receives one stable public code, resolves through the active English or Traditional Chinese UI language and
uses that same code in the UI, structured logs, privacy-safe diagnostics and support guidance. Raw exception details
remain bounded technical context rather than the primary user message.

Exit criteria:

- Every known user-facing warning and error has exactly one unique, documented and compatibility-stable code.
- Main window, Settings and tray surfaces resolve messages from the active UI language without parsing English text.
- English and Traditional Chinese resource parity, code uniqueness, fallback and immediate language changes have
  automated coverage.
- Structured logs and privacy-safe diagnostics carry the same code shown to the user without exposing media metadata,
  complete target identity, secrets or unrelated full paths.
- Missing translations and unknown codes fail to a safe, identifiable fallback while routing and recovery behavior
  remain unchanged.

Status: complete for [GitHub Issue #64](https://github.com/Command1264/MediaLock/issues/64). Phase 17 does not add
telemetry or automatic remote reporting.

## Phase 18 — Provider-neutral playback-rate estimation

Keep Now Playing position and time labels aligned when a provider omits a usable playback rate or changes rate during
playback. A provider-neutral Core Module observes authoritative timeline samples with monotonic timestamps and
resolves one Effective Playback Rate as Reported, Estimated or Fallback. Application owns observation and catalog
projection; WPF consumes the resolved value without owning sample windows, confidence or hysteresis.

Exit criteria:

- A valid Reported Playback Rate remains authoritative and is distinguishable from a missing value; missing or invalid
  input no longer silently means reported 1×.
- Estimation uses only same-target, Playing, monotonic authoritative observations and converges for 0.5×, 1×, 1.5×
  and 2× within documented confidence and latency bounds.
- Mid-play rate changes replace the prior estimate only after sustained evidence, while jitter, duplicate snapshots and
  quantized labels do not create visible oscillation.
- Seek, Pause, Stop, Changing, Recovery, reconnect, target／document replacement, invalid bounds and position jumps
  reset confidence before a new estimate can affect presentation.
- Different provider-qualified Media Targets never share samples, and Effective Playback Rate never affects Router
  policy, identity, Recovery, persistence or command dispatch.
- Deterministic tests use a fake monotonic clock without sleeps. A bounded manual matrix checks slider cadence, time
  labels, language／theme parity and competing-source isolation.

Status: planned for [GitHub Issue #65](https://github.com/Command1264/MediaLock/issues/65). See the
[Phase 18 implementation plan](phase-18/playback-rate-estimation-plan.md) and
[ADR 0009](adr/0009-separate-reported-and-effective-playback-rate.md).
