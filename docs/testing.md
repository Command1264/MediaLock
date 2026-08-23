# Media Lock Testing Strategy

## 1. Principles

- Test Core transitions and route decisions deterministically without Windows APIs.
- Test every Windows adapter at its actual boundary; mocks cannot prove key consumption or GSMTC interoperability.
- Treat hardware, application and Windows-version support as an explicit matrix.
- A relevant failing test blocks completion; unsupported cases are documented rather than silently skipped.

## 2. Automated layers

### Unit tests

Cover:

- Routing Mode transitions and unlock behavior.
- Route Decision for missing, ambiguous and unsupported targets.
- Session Fingerprint scoring without treating track metadata as identity.
- Recovery timeout, same-app candidates and each Fallback Policy.
- Stale/out-of-order events, cancellation and idempotent refresh.
- Settings validation, schema migration and corrupt-data handling.
- ViewModel projections and command enablement without WPF windows where practical.

Use a fake clock and immutable fixtures. Every state transition should assert both new state and requested effects.

Phase 1 tests the router through `IMediaRouter.DispatchAsync` with an in-memory `IMediaController` adapter. The
suite covers Windows Auto, App Lock and Session Lock decisions; unique and ambiguous recovery; all Fallback Policy
values; stable Recovery epochs and stale deadlines; fingerprint confidence ranking; idempotent immutable catalog
refreshes; unsupported commands and failed controls; submission ordering, maximum concurrency and queued
cancellation. Tests do not call reducer helpers or inspect the router's internal queue.

Phase 2 tests application coordination through `IMediaLockApplication`: catalog snapshots enter the real router,
UI intents lock and route, and Recovery deadline effects apply fallback without ViewModel coordination. ViewModel
tests use only public binding properties and async commands. Windows adapter tests cross `IMediaSessionCatalog` and
`IMediaController`, replacing only the external WinRT manager/Session boundary. Regression coverage also verifies
ordered application projection under concurrent dispatch, stale-target removal after terminal catalog failure and
capacity-one coalescing of burst GSMTC events, including cancellation of a blocked refresh during disposal.

Phase 3 tests settings schema migration, corrupt-file preservation, atomic settings/runtime-state round trips,
bounded diagnostic rotation, reversible current-user login startup, current-user single-instance activation, tray
state/commands and settings ViewModel commands.

Phase 4 tests startup policy at `IMediaLockApplication`: Windows Auto ignores a saved lock, while Session Lock
requires a valid persisted fingerprint and a unique acceptable candidate. Core tests keep ambiguous candidates in
Recovering. Windows adapter tests verify suspend disposal, old-subscription removal, Reacquiring/Unavailable
snapshots, exactly three bounded resume attempts and recovery on a later resume. ViewModel tests verify those
catalog states are observable without exposing media metadata in diagnostics.

Phase 5A tests App Lock through the existing router and application interfaces. Startup requires matching App Lock
settings and runtime state, interactive commands submit source application identity through the application, and
main/tray ViewModels distinguish `App Locked` from Session Lock. Existing Core tests own the deterministic
playing/recency/stable-key candidate policy.

Phase 5B tests Priority Rules through the same public seams. Core coverage proves enabled-rule ordering, the
Windows Current Session fallback and invalid duplicates. Application coverage proves startup without a persisted
Locked Target, interactive activation and persistence of every successful explicit Routing Mode as the startup
mode, including preservation of the prior startup mode when settings or Locked Target persistence fails. Settings
tests cover its read-only startup-mode projection, preservation of unsaved edits during a mode-only update, plus Priority Rule add,
enable/disable, move, remove and schema-v3 round trips, including v1/v2 migration. Main/tray ViewModels expose the
mode explicitly; tray Windows Auto remains a current-run action.
Application regression coverage also keeps runtime autosave suppressed after that tray override across later media
commands, and restores the prior runtime document if the startup-settings commit fails.

Phase 6 tests packaging at the release-command seam. `tests/packaging/Publish-ReleaseCandidate.Tests.ps1` invokes
the public publish script against an isolated temporary output, verifies the versioned ZIP/manifest/checksum set,
recomputes SHA-256 independently, expands the archive and requires exactly one correctly versioned
`MediaLock.exe`. Test artifacts must opt into dirty-source publication and disclose that state in their manifest;
formal candidates reject dirty worktrees.

Phase 7A tests language-preference validation and schema-v3 migration through the settings repository seam. App
tests cover supported-culture resolution, English fallback, Traditional Chinese resource lookup, Settings language
choices, language-native choice names, successful-save application and failed-save suppression. A manual UI check
must confirm that main-window text, Settings, playback/status labels, accessibility names and notification-area
commands switch immediately after Save without restarting routing or duplicating a media command.

Phase 7B tests theme-preference validation, schema-v4 migration, Windows-theme and DWM-frame mapping, Settings choices,
successful-save application, failed-save suppression, Cancel discard behavior and the fixed frameless Settings
contract. Build-time XAML compilation covers both palette and shared control dictionaries. A manual WPF smoke test
must exercise Light, Dark and Windows theme in English and Traditional Chinese, verify the native title-bar theme,
main-window minimum-size layout, keyboard focus, Settings scrolling/dragging/Cancel/Escape, owner disablement while
Settings is open, a direct Alt+Tab-close foreground return, notification-area lifecycle and one routed Play/Pause
without duplicate action.
Shared component changes additionally follow the interaction-state and minimum-size verification in
`docs/ui-design-language.md`; stable geometry and required template parts receive WPF contract coverage where
practical.
Settings coverage verifies that Recovery timeout input accepts only finite ordinary decimal values from 0 through
300, reports invalid input through WPF validation immediately and prevents Save while invalid.
The four Routing Mode controls must expose exactly one checked state derived from Router Mode; switching and Recovery
must not resize the controls or remove the selected semantic.

Phase 7C tests bounded artwork validation independently from WinRT, target-owned artwork/timeline projection and
deterministic playing-position interpolation with a fake `TimeProvider`. WPF coverage verifies that the current-target
surface contains a non-interactive progress indicator and optional artwork presentation. Manual coverage uses Brave
YouTube and YouTube Music to verify artwork changes, play/pause timeline behavior, Session recreation, Light/Dark,
English/Traditional Chinese and unchanged single-dispatch media-key routing. Seek is recorded only as a separate
capability probe and is not part of the production UI.

Phase 8A unit tests the Probe's invariant numeric parsing, finite/non-negative validation, 100-nanosecond tick
conversion, invalid timeline handling and inclusive timeline bounds. Hardware-assisted evidence must not infer
movement from `TryChangePlaybackPositionAsync` alone: it records capability, return value and observed timeline for
Brave YouTube Music and ordinary Brave YouTube in Playing and Paused states. Existing physical-key routing is checked
once to prove the probe-only change did not alter capture or dispatch.

Phase 8B tests the production interfaces in vertical slices. Core coverage proves the parameterized Media Command
invariant, Seek capability, target resolution, inclusive timeline bounds and no controller call for unsupported,
missing or out-of-range requests. Application coverage proves the exact command stays serialized and observable.
Windows adapter coverage proves capability mapping and ticks translation. ViewModel and WPF coverage proves local
preview, one commit per gesture, asynchronous timeline confirmation, bounded rollback, target-change cancellation,
disabled states, localization, theme and accessibility without adding a Seek media-key mapping.
WPF and ViewModel coverage also verifies that the localized error card has an explicit dismiss action.
Selection-bookmark coverage publishes repeated catalog and Recovery snapshots across all four Routing Modes. It replaces
the locked Session Key, verifies both lock modes select the Router-resolved successor, and proves explicit selections are
never overridden. WPF coverage also replaces the ephemeral Key of a still-present, unrelated selected row while another
target enters Recovery. A unique source successor remains selected; missing or ambiguous candidates remain unselected,
and timeout clears the bookmark without falling back to the first row.

Phase 8C tests the physical-input path below the native callback. Windows coverage proves virtual-key mapping,
accepted/pass-through decisions, repeat suppression and matching Key-up consumption. Application coverage proves
capability and Recovery gates, immediate settings disablement, bounded-queue backpressure, fault containment and
capture-time target preservation. Core coverage proves an input cannot route after the Active Target changes.
Settings coverage proves schema-v1-v5 migration enables interception and the localized Settings switch round-trips.
The native hook installation itself remains hardware-assisted because an automated test must not install a global
keyboard hook into the developer's interactive desktop.

Phase 9 re-runs the complete local gate and the public packaging test for `0.2.0-rc.2`. Packaging coverage additionally
requires the App project's default `Version` and `InformationalVersion` to match the candidate version, preventing a
normal build from retaining stale prerelease metadata even when the publish command overrides MSBuild properties.
The formal archive must come from a clean reviewed commit; host and Windows Sandbox results apply only to the source
commit and SHA-256 recorded for that archive.

### Integration tests

Cover:

- GSMTC manager acquisition, Session enumeration and event lifetimes.
- Control capability checks and failed `Try*Async` outcomes.
- JSON atomic replacement and `%LocalAppData%` path behavior in an isolated test directory.
- single-instance coordination and graceful shutdown.
- WPF binding smoke tests for the critical lock/unlock path.

### End-to-end and hardware-assisted tests

Run the packaged application with real media sources and physical media keys. Capture application logs, target
playback state before/after, competing playback state and whether Windows also processed the command.

The Phase 8C production protocol and result table live in
[Phase 8C global media-key smoke](phase-8/media-key-interception-smoke.md).

## 3. Critical user paths

### Competing application

1. Play YouTube Music in Brave and lock its Media Session.
2. Start Spotify or Discord media so Windows Current Session changes.
3. Press Play/Pause and Next.
4. Verify only the Locked Target changes once.

### Recovery

1. Lock a Brave YouTube Music Session.
2. Close or restart Brave.
3. Verify Media Lock enters Recovering without crashing.
4. Reopen the intended media source.
5. Verify it re-locks only when the candidate policy accepts it.

### Lifecycle

1. Lock a target, then sleep and resume Windows.
2. Verify manager/subscriptions are reacquired and no event is duplicated.
3. Restart or intentionally terminate Media Lock.
4. Verify valid settings and runtime state restore according to startup policy.

### Phase 3 desktop lifecycle smoke test

1. Start `MediaLock.App`, confirm the main window and one notification-area icon appear; open Settings from the
   upper-right toolbar and confirm only one independent settings window appears.
2. Close the window with close-to-tray enabled; confirm the process and icon remain, then reopen from `Show Media Lock`.
3. Start a second `MediaLock.App`; confirm it activates the first window and exits without adding another icon.
4. Toggle `Start with Windows`, save, verify the Settings window closes and the current-user Run entry is added;
   disable and verify it is removed. A failed save must leave Settings open with an actionable error.
5. Open Settings from the tray, route one command, switch to Windows Auto, then choose `Exit`; confirm the icon disappears and no
   Media Lock process remains.
6. Reopen the app and confirm `settings.json`, `state.json` and bounded `logs\*.jsonl` remain readable.

Recorded production-boundary evidence: [Phase 3 manual smoke — 2026-08-22](phase-3/manual-smoke-2026-08-22.md).

### Phase 5A App Lock smoke test

1. Play YouTube Music and ordinary YouTube in the available Brave/PWA surfaces, then lock the selected source with
   `Lock app`; verify the UI and tray show `App Locked`.
2. Change Windows Current Session and route one supported command; verify only the resolved App Lock candidate
   changes once.
3. Stop the locked application's Session, verify `Recovering`, then recreate it and verify App Lock resolves again.
4. Save App Lock as the default, exit from the notification area, relaunch, and verify the saved application is
   restored without binding a different source application.

Record results in [Phase 5A manual smoke](phase-5/manual-smoke.md).

### Phase 5B Priority Rules smoke test

1. Add the ordinary Brave source below the Brave YouTube Music PWA source and save. Activate `Priority Rules` on
   the main window, restart, and verify the UI, tray and read-only Settings summary show `Priority Rules`.
2. With both sources available, route Play/Pause and verify only YouTube Music changes once.
3. Disable or remove the YouTube Music rule, save and restart; verify ordinary YouTube now receives one command.
4. Make every enabled rule unavailable while another Windows Current Session exists; verify that current Session
   receives one command and Media Lock remains in `Priority Rules`.

Record results in [Phase 5B manual smoke](phase-5/priority-rules-smoke.md).

## 4. Compatibility matrix

Record Windows build, application version, input device/backend and result for:

| Category | Required MVP coverage |
| --- | --- |
| Browser | Brave + YouTube Music; Chrome + YouTube Music; Edge + YouTube Music |
| Desktop player | Spotify Desktop; VLC; Windows Media Player or current Microsoft equivalent |
| Competing media | Discord; Steam where it exposes GSMTC |
| Multi-session | YouTube Music plus YouTube; multiple media tabs in one browser |
| Playback state | Playing; paused; stopped; application exit |
| Session lifecycle | Refresh; browser restart; app restart; Session recreation |
| Windows lifecycle | Sleep/resume; lock/unlock; application restart; crash recovery |
| Input | Play/Pause; Next; Previous; Stop across supported hardware backends |

Unavailable or non-cooperating GSMTC applications are recorded as compatibility results, not automatically treated
as Media Lock defects. Claims of support require a passing row on a named environment.

## 5. Phase 0 input protocol

For each candidate backend:

1. Record keyboard/device and Windows build.
2. Observe baseline behavior with Media Lock stopped.
3. Start the probe, select one Session and create a competing Windows Current Session.
4. Send each physical Media Command at least ten times, including key repeat where applicable.
5. Count target actions, competing actions, missed actions and duplicate actions.
6. Repeat after focus changes, lock/unlock and sleep/resume.
7. Record whether ordinary-user execution is sufficient.

Acceptance requires one target action per supported key action, zero competing actions after consumption and no
unbounded resource or subscription growth. The exact supported backend/device scope follows the evidence.

## 6. Build and release gates

Once projects exist, the standard verification pipeline should include formatting, compiler warnings as configured,
static analysis, unit/integration tests, Release build and publish inspection. The self-contained single-file output
must be exercised on a clean supported Windows machine, including cold start, tray icon/resources, settings writes,
logs, startup registration and uninstall/cleanup instructions.

Run the repeatable local Phase 6 gate from the repository root:

```powershell
dotnet format MediaLock.sln --verify-no-changes --no-restore
dotnet test MediaLock.sln --configuration Release --no-restore
dotnet build MediaLock.sln --configuration Release --no-restore
& .\tests\packaging\Publish-ReleaseCandidate.Tests.ps1
```

After committing the reviewed source, produce the provenance-clean candidate with
`eng/Publish-ReleaseCandidate.ps1`; see [Release candidate runbook](release-candidate.md). GitHub Actions capacity is
not assumed by this gate.

The formal `0.2.0-rc.1` candidate passed this gate on Windows Sandbox on 2026-08-23. Its exact source commit, archive
digest, environment and results are preserved in [Phase 6 packaged validation](phase-6/host-smoke.md). The same gate
was completed independently for the formal `0.2.0-rc.2` candidate on 2026-08-24; its exact identity and results are
preserved in [Phase 9 packaged validation](phase-9/release-candidate-smoke.md). Evidence does not transfer between
commits or digests.

## 7. Manual evidence

When a check cannot be automated, preserve a repeatable test record containing environment, exact steps, expected
result, actual result, logs and date. “Works on my machine” without those fields is not acceptance evidence.
