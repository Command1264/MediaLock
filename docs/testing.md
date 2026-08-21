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

## 7. Manual evidence

When a check cannot be automated, preserve a repeatable test record containing environment, exact steps, expected
result, actual result, logs and date. “Works on my machine” without those fields is not acceptance evidence.
