# Phase 8A Seek capability probe

Date: 2026-08-23

## Scope

- Probe-only absolute Seek using GSMTC ticks.
- No WPF slider, Core command, hotkey, persistence or production routing change.
- `accepted` is recorded separately from actual observed movement.

## Environment

| Item | Value |
| --- | --- |
| Probe source under manual test | `2f9a392` (`feat: validate GSMTC seek capability`) |
| Windows | Windows 11 Pro 25H2, build 26200.9168, x64 |
| Brave | 151.1.93.138 |
| .NET SDK | 10.0.400 |
| Keyboard | ASUS ROG STRIX FLARE mechanical keyboard |
| Input backend | Probe low-level media-key hook, ordinary-user execution |
| Sources | Brave YouTube Music PWA (`Brave._crx_cinhimbnkkghhklpknlkffjgod`) and ordinary Brave YouTube (`Brave`) |

## Repeatable procedure

Run the Probe from its Worktree:

```powershell
Set-Location 'D:\Code\C#\MediaLock\worktrees\feat-phase-8a-seek-probe'
dotnet run --project 'src\MediaLock.Probe\MediaLock.Probe.csproj' --configuration Release
```

| Step | Command or action | Expected result | Actual result |
| --- | --- | --- | --- |
| Discover | `list` | Both sources appear and advertise Seek | Pass; both reported `seek=True` |
| No target | `clear`, `seek 30` | Skip before GSMTC with an actionable reason | Pass; `seek skipped: no selected session` |
| YouTube Music Playing | `select 1`, `play`, `seek 30`, `seek 90` | Each request is accepted and later publishes the requested position | Pass; events reached 30 s and 90 s |
| YouTube Music Paused | `pause`, `seek 60` | Position reaches 60 s and playback remains Paused | Pass |
| YouTube Music invalid | `seek -1`, `seek 999999` | Negative input and out-of-range position are rejected before GSMTC | Pass |
| Ordinary YouTube Playing | `list`, `select 2`, `play`, `seek 30`, `seek 90` | Each request is accepted and later publishes the requested position | Pass; events reached 30 s and 90 s |
| Ordinary YouTube Paused | `pause`, `seek 60` | Position reaches 60 s and playback remains Paused | Pass |
| Ordinary YouTube invalid | `seek 999999` | Out-of-range position is rejected before GSMTC | Pass |
| Recovery | `select 1`, reload the YouTube Music PWA, `seek 60` after recovery | Enter bounded Recovery, reselect the replacement Session and retain Seek | Pass; recovered in about 1.25 s and event reached 60 s |
| Input regression | `hook on`, press physical Play/Pause, then `hook off` | One route per input; selected source changes and competing source does not | Pass across six presses |
| Finish | `exit` | Hook is disabled and Probe exits cleanly | Pass |

## Application matrix

| Source | State | Advertises Seek | Request | API result | Observed movement | Competing source unchanged |
| --- | --- | --- | --- | --- | --- | --- |
| Brave YouTube Music PWA | Playing | Yes | 30 s and 90 s | Accepted | Timeline events reached 30 s and 90 s | Pass |
| Brave YouTube Music PWA | Paused | Yes | 60 s | Accepted | Timeline event reached 60 s while Paused | Pass |
| Brave ordinary YouTube | Playing | Yes | 30 s and 90 s | Accepted | Timeline events reached 30 s and 90 s | Pass |
| Brave ordinary YouTube | Paused | Yes | 60 s | Accepted | Timeline event reached 60 s while Paused | Pass |

## Safety and lifecycle matrix

| Scenario | Expected | Result |
| --- | --- | --- |
| Negative seconds | Rejected before GSMTC | Pass (`seek -1`; parser behavior is source-independent and unit-tested) |
| Position beyond current timeline | Rejected before GSMTC | Pass on both sources (`seek 999999`) |
| No selection | Command skipped with an actionable reason | Pass |
| Session recreation | Selection follows existing bounded Recovery behavior | Pass; recovery completed in about 1.25 s and Seek remained accepted |
| Existing physical Play/Pause | Routed once to the selected target | Pass; six inputs each produced exactly one accepted route |

## Observations

- Both tested Brave sources advertise playback-position support.
- `TryChangePlaybackPositionAsync` returned before the Session's published timeline changed. The immediate observation was
  therefore the previous value; a timeline event reached the requested value roughly 10–25 ms later in every tested
  case. Production confirmation must be event-driven rather than interpreting the immediate read as failure.
- Later timeline changes at YouTube Music 01:10 and ordinary YouTube 12:17 were confirmed as operator actions, not
  unsolicited reversions.
- The non-selected Brave source remained unchanged throughout each source's Seek checks.
- No crash, queue error or duplicate route was observed.

## Key log evidence

```text
controls(toggle=True, next=True, previous=True, stop=True, seek=True)
controls(toggle=True, next=False, previous=False, stop=True, seek=True)
ERROR: ROUTE seek skipped: no selected session.
ROUTE seek -> Brave._crx_cinhimbnkkghhklpknlkffjgod: capability=enabled; API=accepted; requested=00:00:30
Selected timeline changed: 00:00:30.0074200.
ROUTE seek -> Brave: capability=enabled; API=accepted; requested=00:01:30
Selected timeline changed: 00:01:30.
ERROR: Seek seconds must be a finite, non-negative number using '.' as the decimal separator.
ERROR: ROUTE seek -> Brave: skipped; Seek position 11.13:46:39 must be between 00:00:00 and 00:16:16.3210000.
Selected session 'Brave._crx_cinhimbnkkghhklpknlkffjgod' was lost temporarily; recovering for up to 2 seconds.
Selected session 'Brave._crx_cinhimbnkkghhklpknlkffjgod' recovered.
INPUT PlayPause; consumed; queued for selected session.
ROUTE PlayPause -> Brave._crx_cinhimbnkkghhklpknlkffjgod: accepted
```

## Phase 8B decision

Proceed to a separately scoped Phase 8B production design. It must remain capability-gated, preserve competing-source
isolation and confirm the resulting position asynchronously from timeline events rather than relying on API acceptance
or the immediate post-request observation alone.
