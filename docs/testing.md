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

Phase 1 tests the router through `IMediaRouter.DispatchAsync` with an in-memory `IMediaTargetController` Adapter. The
suite covers Windows Auto, App Lock and Session Lock decisions; unique and ambiguous recovery; all Fallback Policy
values; stable Recovery epochs and stale deadlines; fingerprint confidence ranking; idempotent immutable catalog
refreshes; unsupported commands and failed controls; submission ordering, maximum concurrency and queued
cancellation. Tests do not call reducer helpers or inspect the router's internal queue.

Phase 2 tests application coordination through `IMediaLockApplication`: catalog snapshots enter the real router,
UI intents lock and route, and Recovery deadline effects apply fallback without ViewModel coordination. ViewModel
tests use only public binding properties and async commands. Windows Adapter tests cross `IMediaTargetCatalog` and
`IMediaTargetController`, replacing only the external WinRT manager/Session boundary. Regression coverage also verifies
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
Saving reordered, enabled, disabled or removed Priority Rules while Priority Rules is active must immediately
recalculate the routed target through `IMediaLockApplication`; restarting or reselecting the Routing Mode is not part
of acceptance.

Phase 15 tests the source-name Module through its presentation Interface. Coverage proves trusted display/host
composition, raw-ID fallback, deterministic collision disambiguation, Main and Settings projection, accessible raw
details and preservation of the exact identity in App Lock and saved Priority Rules. Windows Adapter tests replace
AppsFolder enumeration with deterministic metadata and prove exact, case-sensitive lookup, one-time loading and safe
catalog failure. Named desktop evidence separately verifies the real Brave YouTube Music PWA and ordinary Brave／Chrome
entries because a fake cannot prove Shell registration.

Settings tests maintain a runtime-consumer matrix. Recovery and Priority Rules must reach the Router immediately,
including cancellation and replacement of an active Recovery deadline when its timeout changes. Login startup must
match the owned Run value and repair an external overwrite after initial synchronization without deleting a
portable-owned value when startup is disabled. Global-key interception and close-to-tray must observe the published
application state,
repeated-pause settings must reset prior observations, and language/theme must apply only after a successful commit.
Every newly introduced setting adds either an immediate-application test at its owning public seam or an explicit,
documented startup-only contract. A repository write by itself is never sufficient evidence of a successful runtime
settings change.

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
WPF and ViewModel coverage also verifies that the localized error card has an explicit dismiss action, that an
ordinary Application state refresh does not redisplay the same dismissed error, and that the message becomes visible
again after the underlying condition clears and recurs. A startup-lock fallback also keeps its already-selected mode
action enabled when the saved startup mode differs, allowing one explicit click to persist the safe fallback.
Resolved-target command coverage keeps Play disabled while Playing, Pause disabled while Paused and Toggle enabled
when advertised; Unknown presentation falls back to capability-only enablement.
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

Phase 10A validates the public feedback boundary without rebuilding the published executable. Parse every Issue Form
as YAML, inspect required field identifiers and verify that referenced labels exist before merge. Check repository-
relative Markdown links from README, support and installation documents. After integration into the repository's
default branch, preview both forms on GitHub and confirm blank issues are disabled. The forms must request exact
environment and competing-source evidence while warning reporters to redact media metadata, secrets and unrelated
settings. Documentation review follows one fresh-download path through digest verification, first run, update,
rollback and removal. GitHub Actions capacity is not required.

Phase 10B tests `DiagnosticSummary` at its public pure-function seam with deliberately private media metadata and
requires exact native line separators plus the documented allowlist. Windows adapter tests cover version/build
projection, Windows 11 normalization, architecture, embedded-signature state, exact clipboard text, log-directory
creation and canonical support targets without opening interactive applications. Settings ViewModel tests replace
both adapters, verify all four actions and actionable failure projection; WPF contract coverage requires the version
facts, four command bindings and accessibility names inside the existing scrollable modal. Manual verification follows
[Phase 10B About and diagnostics smoke](phase-10/about-diagnostics-smoke.md) in English/Traditional Chinese and
Light/Dark. This executable change requires a separately built `0.2.0-rc.3` clean-host and Sandbox gate.

Phase 10C repeats the complete local and packaging gates from a clean reviewed `0.2.0-rc.3` source commit. Host and
Windows Sandbox results must independently match that commit and archive digest, exercise the new About/diagnostics
surface, and confirm the existing single-instance, Settings, startup, tray, GSMTC and global-media-key paths. No
`0.2.0-rc.1` or `0.2.0-rc.2` evidence transfers.

Phase 10D first makes stable-version acceptance red at the packaging seam, then changes only release identity,
packaging compatibility and documentation. Automated coverage requires project Version and InformationalVersion
`0.2.0`, stable About/diagnostics classification, continued acceptance of `-rc.N`, dirty-source rejection, exact
artifact names, one executable, ProductVersion/FileVersion, manifest flags and independently recomputed checksums.
Run the complete local gate and two-axis review before creating the clean formal artifact. Local-host and Windows
Sandbox evidence must match that artifact's exact source commit and digest and repeat the RC3 critical paths; no RC
runtime result transfers. Preserve `release/0.2` as the current stable hotfix baseline after merge and publication.

Phase 11A uses RED → GREEN coverage at the public Core and Application seams. A decision matrix covers Off and Keep
Playing against observed Playing, Paused, Stopped, Closed and unavailable states. Only Keep Playing plus Paused yields
an explicit Play correction; no policy yields Pause, and Stopped or Closed playback is never restarted. Application
tests use controller observations and fresh catalog snapshots to prove captured-target protection, one in-flight
correction, confirmation, two-attempt exhaustion, Media Lock Pause/Toggle/Stop clearing and Play/Next/Previous
preservation. Catalog loss, fallback, ambiguity, Recovery, target replacement, suspend and shutdown must cancel,
suspend or clear work without dispatching to a competing Session. Power Suspend explicitly turns Keep Playing Off;
resume must not issue Play or re-arm it. Process restart begins at Off.
Workstation lifecycle tests distinguish power suspend from Session Lock/Unlock, require an unlock-triggered GSMTC
refresh, and cover lock-screen Pause arriving on either side of the Unlock event. That explicit override clears Keep
Playing with zero correction commands; an unchanged Playing refresh preserves the policy and closes the attribution
window so a later desktop Pause is corrected normally. Power Suspend tests cover Windows Auto and Priority Rules,
require Off at the Suspended observation and prove that a paused post-resume target receives zero Play commands.
Repeated-pause tests use a controllable clock to prove that three distinct Playing-to-Paused transitions in the
default five-second window release Keep Playing, leave the third pause uncorrected and emit a Released state. They
also prove window expiry, duplicate Paused suppression, Changing-to-Paused buffering exclusion, sequence resets and
settings bounds. ViewModel tests verify one optional system-sound request and a localized notice that clears after
five seconds or disposal.

Automatic-routing Recovery tests cover Windows Auto and Priority Rules separately. When the Armed Playback Target
temporarily disappears, both must publish Suspended without dispatching to the competing Session. A unique acceptable
successor that becomes Active Target re-arms Keep Playing and receives at most one correction; ambiguous same-source
successors and an inactive successor remain Suspended with zero correction commands. A long-running-target case proves
that live observations refresh the fingerprint before a later recreation. The existing unrelated-target test continues
to prove that an Active Target change while the original Session is still present clears the policy.

Phase 11A ViewModel and WPF contract coverage verifies the current-target placement, exactly one of Off/Keep Playing,
stable geometry, accessibility names, keyboard operation and English/Traditional Chinese plus Light/Dark rendering.
Hardware-assisted coverage uses YouTube Music and ordinary YouTube simultaneously. It changes playback both through
Media Lock and outside it, then repeats Next, Previous, reload/Recovery, focus changes, lock/unlock, sleep/resume and
physical Play/Pause. Evidence records every requested and observed transition; accepted GSMTC calls alone are not proof
that the state was enforced.

Phase 11B is a separately scoped feasibility test. Unit tests cover metadata/capability projection, event translation,
self-session exclusion, target capture, one-dispatch behavior and disposal with a fake media-surface adapter. The
Windows probe records whether the Media Lock SMTC Session exists, whether Windows makes it current, what the native
surface renders, and which Session actually changes after each button or seek action. Test YouTube Music against a
competing ordinary YouTube Session through reload/Recovery, target changes, lock/unlock, sleep/resume and shutdown on
named Windows builds. A passing adapter lifecycle does not imply current-session selection. Phase 11C is blocked unless
the evidence supports a precise compatibility claim; unreliable selection produces a limit or reject record instead.

Phase 12A tests the installer and portable archive as two containers around one reviewed payload. Packaging coverage
first makes the fixed Inno `AppId`, per-user privilege mode, stable install directory, Start Menu shortcut and uninstall
metadata requirements fail, then verifies both artifacts record the same payload hash and source commit. The release
command must retain dirty/changing-source rejection, refuse partial final output, independently verify every digest and
fail clearly when the pinned Inno compiler is absent or unexpected. Startup tests cover exact quoted-command matching
and prove uninstall cannot remove a Run value owned by a portable executable.

The clean-Windows Phase 12A gate starts from an ordinary user and separately records cold install, Search launch,
Installed apps metadata, runtime smoke, opt-in login startup, previous-version in-place upgrade, controlled
cancellation/failure, supported downgrade or an actionable block, uninstall with retained user data, and portable
coexistence. Installer, ZIP, source commit and Windows build evidence are inseparable. An unsigned Setup remains an
explicit test fact, and observed Inno behavior must not be generalized into MSI-level transactional rollback.

The initial implementation pins official Inno Setup `6.7.3`. Local RED → GREEN evidence covers owned and nonowned
startup values, early non-UI cleanup command parsing, isolated ZIP/Setup publication, schema-2 manifest hashes and
unsigned installer metadata. A reversible current-user transaction smoke installed without elevation, created the
fixed executable/Start Menu/Installed apps entries, removed an owned Run value, preserved a portable-owned value and
left existing user data intact after uninstall. This host evidence does not replace the remaining clean-Windows,
upgrade, downgrade or full runtime matrix.

`tests/packaging/WindowsSandbox-InstallerSmoke.ps1` is the PowerShell 5.1-compatible clean-environment transaction
gate. It consumes only a read-only five-file artifact set and writes a JSON result to a separately mapped directory;
it never treats an installed executable as evidence of a matching payload without independently hashing it.

`tests/packaging/WindowsSandbox-InstallerUpgradeSmoke.ps1` requires explicit `OlderVersion`, `NewerVersion` and pinned
older-installer SHA-256 parameters and selects exactly those manifests even when the artifact root contains other
versions. Its shared PowerShell 5.1-compatible release parser orders stable and `-rc.N` versions without a `[version]`
cast. Both installer files must match their manifests, and the older file must also match the caller's trusted digest.
The script installs the older version, preserves exact settings/state bytes, an installed-path startup command and a
user-data marker, upgrades in place, performs same-version repair, then requires the older installer to be rejected
with Inno exit code 7. Before that blocked transaction it captures the installed EXE and shortcut SHA-256 values, the
complete Media Lock uninstall-registration properties, startup command, settings/state and retained-marker hashes;
the post-attempt snapshot must match every field exactly.

`tests/packaging/WindowsSandbox-InstallerCancellationSmoke.ps1` uses the same explicit version and pinned-digest seam
and has `Prepare` and `Verify` phases around a visible installer action. The recorded gate cancels on the Ready page
before installation begins, requires exit code 2 and persists a trusted preparation snapshot beside the result. The
Verify phase binds that snapshot to both selected installer hashes, then proves the installed EXE, Start Menu shortcut,
complete uninstall registration, startup command and user data remain byte-for-byte unchanged. It does not claim that
cancellation during file extraction was observed or that Inno provides MSI-level transaction rollback.

The transaction gate passed on Windows Sandbox on 2026-08-25 for source commit
`6233da8bab35e6fcde0858d1fa0a58fe5babfba6`. It independently matched the ZIP and unsigned Setup digests, matched
the installed payload hash, created the Start Menu and Installed apps entries without enabling startup by default,
removed an installer-owned startup value, preserved a portable-owned startup value, retained user data and finished
with no Media Lock process. This result covers the scripted install/uninstall transaction only; indexed Search, full
runtime/media behavior, actual login restart, upgrade, downgrade and controlled cancellation remain separate gates.

The same artifact also passed a visible ordinary-user Sandbox smoke: the English Setup wizard opened without UAC,
used the fixed per-user destination, launched without a separate .NET installation prompt, opened Settings, appeared
in the Windows search panel and restored the existing process, and exposed a notification-area icon that restored the
window on double-click. Windows Sandbox reported that search indexing was disabled, so indexed keyword search remains
a host/manual gate rather than an inferred pass from the visible shortcut.

The user completed that host/manual gate on 2026-08-25 with the same installer payload. Windows Search discovery,
single-process launch, Tray restore, startup registration, actual sign-out/sign-in startup, Play/Pause, Next,
Previous and Recovery all passed. The competing ordinary YouTube source remained unchanged. Uninstall completed,
user data remained available, and no error or crash was reported. Test-only `0.2.0` and `0.2.1` artifacts from clean
source commit `ed05c2742bdc6f3b0d5760406c6c3c410533ff9d` then passed the Sandbox matrix: both installer hashes matched
their manifests, in-place upgrade retained data/startup state, and the older installer was intentionally blocked with
exit code 7. Cancelling on the Ready page returned exit code 2 and left the old installation unchanged. Cancellation
during file extraction was attempted, but the single-file payload completed before cancellation was delivered, so
that stronger rollback claim remains unverified.

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

### Phase 15 human-readable source identity smoke test

1. Run the Phase 15 candidate with ordinary Brave YouTube and the installed Brave YouTube Music PWA both publishing
   Sessions. Verify the Main list and current target use distinct friendly names while raw IDs remain available as
   details.
2. Open Settings and verify the add-rule choice and existing Priority Rules use the same names. Add／reorder／save the
   YouTube Music rule and confirm the exact raw ID remains in `settings.json`.
3. Activate each Routing Mode, route Play/Pause once and verify the intended source changes once while the competing
   source remains unchanged. Refresh the PWA and verify Recovery still follows the original identity.
4. Repeat the presentation check in English／Traditional Chinese, Light／Dark and the supported minimum main-window
   size. Missing metadata must visibly fall back to the raw ID without error or routing loss.

Record results in [Phase 15 source identity evidence](phase-15/human-readable-source-identities.md).

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

For Phase 12B publish-footprint work, first run the fast measurement-contract test:

```powershell
& .\tests\packaging\Measure-PublishFootprint.Tests.ps1
```

Then close every running Media Lock instance and produce an ignored, machine-specific comparison report:

```powershell
& .\eng\Measure-PublishFootprint.ps1 `
    -OutputRoot '.\artifacts\phase-12b-footprint' `
    -ColdStartIterations 15 `
    -WarmStartIterations 15
```

Add `-IncludeLocaleCandidates` only when running the complete English／Traditional Chinese／Windows-language fallback
matrix. The benchmark alternates variant order and uses isolated bundle extraction caches, but does not flush the
Windows file cache; preserve a separate reboot-based first-launch smoke for the selected candidate. Generated binaries,
cache directories and raw host reports remain under ignored `artifacts/` and are not release evidence until tied to an
exact reviewed source commit. See [Phase 12B footprint plan](phase-12/footprint-optimization-plan.md).
Preserve the sanitized exact-commit result using the structure in
[Phase 12B host footprint benchmark](phase-12/host-footprint-benchmark.md); do not commit executables, extraction caches
or machine-specific absolute paths.

The accepted Phase 12B profile passed its i7-8700 host smoke and fresh Windows Sandbox gates on 2026-08-25. The
Sandbox artifact was built from clean commit `e277736d2abb4586a37af2ef1f961c307d8a4243`; its manifest declared schema 3
and `singleFileCompressed: true`. The transaction smoke verified hashes, installed payload identity, Start Menu and
Installed apps registration, default startup behavior, owned／portable startup cleanup boundaries, user-data retention
and uninstall cleanup. A separate fresh Sandbox launch reached a visible main window and retained one process after a
second launch. See the exact sizes, digests, host results and explicitly skipped direct reboot A/B pair in the
[Phase 12B host footprint benchmark](phase-12/host-footprint-benchmark.md).

Phase 13 uses the frozen [0.3.0-rc.1 release-candidate plan](phase-13/release-candidate-plan.md). Before the candidate
can consume the installer transition gate, the PowerShell 7 and Windows PowerShell 5.1 contract test must prove that
both Sandbox scripts accept an explicitly named stable predecessor and prerelease successor and reject invalid or
ambiguous pairs. Candidate evidence covers both the exact public portable `0.2.0` compatibility path and generated
installer-to-installer version transitions. The formal ZIP and Setup, host checks and clean Windows Sandbox checks must
all identify one reviewed source commit and independently matching hashes.

Because GitHub Actions capacity is unavailable, Phase 13B requires the full local automated gate. It does not infer a
pass from earlier Phase 11／12 commits, and it does not publish until separately authorized. Public candidate assets
are ZIP and Setup only; record their hashes in the GitHub Prerelease body while retaining manifest and standalone
checksum files as local provenance evidence.

Phase 14 uses the frozen [0.3.0 stable-release plan](phase-14/stable-release-plan.md). Stable identity must first fail at
the version, About classification, artifact-name and packaging seams, then pass with ProductVersion `0.3.0` and
FileVersion `0.3.0.0`. PowerShell 7 and Windows PowerShell 5.1 tests must order public `0.3.0-rc.1` below `0.3.0`
without `[version]`, then cover RC1-to-stable in-place upgrade, stable repair, stable-to-RC1 downgrade rejection and
Ready-page cancellation with payload, registration, startup and user-data invariants.

The complete local automated gate, two-axis review, formal ZIP／Setup inspection, i7-8700 exact-artifact host matrix and
fresh Windows Sandbox matrix all run again for stable. Candidate source, ProductVersion and hashes are different
evidence and do not transfer. The real public portable `0.2.0` data path and public RC1 Setup path are distinct
predecessors and must both be represented. Real sign-out/sign-in startup may close only the Sandbox-impossible row on
the persistent host. Preserve results in `phase-14/stable-release-smoke.md`, create `release/0.3` only after the exact
stable source/artifacts pass, and keep `release/0.2` during Phase 14.

After committing the reviewed source, produce the provenance-clean release artifact with
`eng/Publish-ReleaseCandidate.ps1`; see [Release artifact runbook](release-candidate.md). GitHub Actions capacity is
not assumed by this gate.

For public feedback metadata, additionally run a local YAML parse and relative-link check. Before merging Issue Forms,
confirm their declared labels exist in the target GitHub repository; label creation is a separate remote write. After
integration into the default branch, open both GitHub Issue creation URLs and confirm the intended form renders before
announcing the intake path. A merge into a non-default integration branch does not activate GitHub Issue Forms.

The current local syntax and formatting check is:

```powershell
Get-Content '.github/ISSUE_TEMPLATE/bug-report.yml' -Raw | npx.cmd --yes yaml@2.8.1 valid
Get-Content '.github/ISSUE_TEMPLATE/compatibility-report.yml' -Raw | npx.cmd --yes yaml@2.8.1 valid
Get-Content '.github/ISSUE_TEMPLATE/config.yml' -Raw | npx.cmd --yes yaml@2.8.1 valid
npx.cmd --yes prettier@3.6.2 --check '.github/ISSUE_TEMPLATE/*.yml'
git diff --check
gh label list --limit 100 --json name --jq '.[].name'
```

Prettier proves that the YAML can be parsed, not that GitHub will apply nonexistent labels. Compare the last command
against every `labels` entry in the forms and the canonical roles in `docs/agents/triage-labels.md`.

The formal `0.2.0-rc.1` candidate passed this gate on Windows Sandbox on 2026-08-23. Its exact source commit, archive
digest, environment and results are preserved in [Phase 6 packaged validation](phase-6/host-smoke.md). The same gate
was completed independently for the formal `0.2.0-rc.2` candidate on 2026-08-24; its exact identity and results are
preserved in [Phase 9 packaged validation](phase-9/release-candidate-smoke.md). Evidence does not transfer between
commits or digests. Record `0.2.0-rc.3` evidence independently in
[Phase 10C packaged validation](phase-10/release-candidate-smoke.md).
Record stable `0.2.0` evidence independently in
[Phase 10D packaged validation](phase-10/stable-release-smoke.md).
Record stable `0.3.0` evidence independently in
[Phase 14 packaged validation](phase-14/stable-release-smoke.md).

### Phase 16A browser-direct Probe

Phase 16A remains outside production packaging. Run its C# framing／protocol tests and dependency-free Node Extension
tests explicitly, run its PowerShell registration contract test under Windows PowerShell 5.1, then syntax-check every
executable Extension script. The fixed public manifest key／Extension ID,
least-privilege permissions and exact Native Host `allowed_origins` are test contracts. A failure is a Prototype
blocker rather than permission to weaken the origin or site allowlists.

After first loading or reloading the unpacked Phase 16A Extension, an eligible page that predates that load must first
reject as `target-unavailable`; reload the page once, then require the next command to be accepted exactly once. This
documents Chrome's declarative content-script lifecycle and must not be hidden by retrying a mutating command.

The named Chrome／Brave／Brave YouTube Music PWA matrix must distinguish command acceptance from observed movement and
record duplicate count, reload／navigation target continuity, Native Host disconnect and competing-source isolation.
Timeout or an unknown outcome must not retry a mutating command. See the
[Phase 16A Probe plan](phase-16/browser-direct-probe-plan.md).

The Extension protocol test also owns the initial-handshake readiness gate: a command may wait for negotiation only
before dispatch, must proceed once after readiness, and must fail on bounded timeout or disconnect. It may never use
that gate to retry a command already posted to the Host.

On Windows, the Probe's Chrome and Brave registration commands share one Chrome-compatible current-user Native
Messaging registry seam. The PowerShell contract test must invoke both public scripts against an isolated registry
root, require the same manifest／registry identity, migrate only an exact legacy Probe-owned value and preserve a
foreign value. It must also remove an exact obsolete Brave-specific Probe value without claiming or deleting a
foreign value at that location. Registration output is content-addressed: the contract must keep one Host process
running while an unchanged registration reuses its output, then switch a simulated new build to a different
executable path without stopping or overwriting the running Host. Manual Brave evidence additionally verifies that
the launched Host's process ancestry reaches `brave.exe`; a matching unused Brave-specific registry value is not
acceptance evidence.

Every future production integration gate also runs two independent compatibility lanes:

1. **No Extension installed** — the complete stable `0.3.0` regression suite remains available through GSMTC, with no
   blocking prompt or browser-integration dependency.
2. **Extension available** — supported browser targets gain exact identity and direct capabilities, while unsupported
   targets and applications retain their existing GSMTC behavior.

Removing or disabling an Extension before startup must behave like provider absence. Disconnecting it after a direct
target was bound must instead exercise explicit target-preserving Recovery and prove that no competing Session is
controlled.

Phase 16B adds a generic Adapter matrix for a directly hosted MP4, cloud-drive MP4, ordinary streaming page, multiple
media elements, same-origin and cross-origin iframe, unsupported／DRM player and revoked site permission. For every
supported row, record the browser profile, Page Binding, document generation, frame and selected Endpoint separately;
page title, media title, origin and browser executable are presentation／scope inputs rather than sufficient identity.

Routing-mode tests use two playable pages in the same browser. Session Lock and page-scoped Priority Rules must route
only to the bound page. App Lock must use its explicit Browser Application Scope and refuse an ambiguous candidate.
Windows Auto may change targets by policy, but its visible selection must still identify the exact page. Reload,
same-origin navigation, cross-origin navigation, tab duplication, tab replacement and browser restart must never
silently rebind a saved lock or rule to the other page.
For the first production Browser Session Lock candidate, any tab `loading` event removes every temporary or exact-site
binding for that tab and never automatically rebinds it. Deterministic Extension tests also require same-tab repeated
authorization to publish the old binding's removal before its replacement, reject stale-document observations,
republish page-originated playback changes and carry bounded playback rate into desktop timeline interpolation.
Application tests lock and route an exact Browser target with a recording runtime repository and require zero
post-lock saves; persistence tests independently reject Session Lock documents without a durable Locked Target before
creating or replacing `state.json`.
See the [Phase 16B generic web media Adapter plan](phase-16/generic-web-media-adapter-plan.md).

### Phase 16C production integration

Production integration follows the staged gates in the
[Phase 16C plan](phase-16/production-integration-plan.md). The disposable Probe now has named Brave generic-media,
cloud-hosted MP4, ordinary streaming and fail-closed unsupported-page evidence. Treat those rows only as entry
evidence: every different production Extension, Native Host executable and package must rerun the named matrix.

At the provider-neutral seam, deterministic tests use independent GSMTC and Browser Adapters and assert only
observable target snapshots and command results. They cover provider-qualified identity, two same-title pages,
capability enforcement, expected-target capture, stale Endpoint rejection, permission revocation, ambiguity,
provider absence, bound-target loss and unknown outcomes without retry. Browser transport identifiers and permission
state remain internal to the Browser Adapter Module and are not test inputs to Router policy.

The Core／Application seam slice additionally proves that an authoritative exact correlation suppresses only its
named GSMTC duplicate, while same-title pages and uncorrelated Brave GSMTC targets remain distinct. Capability and
expected-target rejection perform zero Adapter calls; every returned outcome performs exactly one call and Outcome
Unknown is never retried. These are deterministic seam tests, not evidence that a production Browser Adapter exists.

Every production candidate runs two independent compositions:

1. with no Extension installed, the complete relevant `0.3.0` GSMTC regression remains available without a prompt,
   settings migration or Browser Adapter dependency; and
2. with the Extension available, direct Session Lock routes Play, Pause, Toggle Play／Pause and bounded Seek only to
   its exact Page Binding, while disconnect after binding preserves the target in Recovery／Unavailable and never
   controls a competing page or GSMTC Session. Provider-neutral input coverage proves a supported physical Toggle is
   consumed and dispatched with the exact captured Browser target rather than being passed through to Windows.
   Extension lifecycle coverage proves Native Messaging disconnect consumes Chromium's scoped last error and never
   calls `disconnect()` on the already-closed port.

App Lock, Priority Rules and Windows Auto each require a later two-page routing-mode matrix before enablement.
Settings migration, authorization UI, Native Host ownership, Extension update/removal and packaged upgrade／repair／
uninstall evidence are independent gates and cannot be inferred from the Session Lock slice.

The Issue #62 candidate now owns the focused .NET Browser tests, dependency-free Extension tests and isolated
current-user registration contract in
[the Browser Session Lock runbook](phase-16/browser-session-lock-candidate.md). The full solution gate remains
mandatory because the no-Extension composition must preserve every GSMTC test. Manual acceptance then runs the six
named lanes in that runbook; Probe results do not transfer. Until those rows pass, the candidate must not be described
as packaged, distributed or production-qualified.

## 7. Manual evidence

When a check cannot be automated, preserve a repeatable test record containing environment, exact steps, expected
result, actual result, logs and date. “Works on my machine” without those fields is not acceptance evidence.
