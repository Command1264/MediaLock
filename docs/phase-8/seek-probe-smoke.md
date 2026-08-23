# Phase 8A Seek capability probe

Date: 2026-08-23

## Scope

- Probe-only absolute Seek using GSMTC ticks.
- No WPF slider, Core command, hotkey, persistence or production routing change.
- `accepted` is recorded separately from actual observed movement.

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
| Negative seconds | Rejected before GSMTC | Pass (`seek -1`) |
| Position beyond current timeline | Rejected before GSMTC | Pass (`seek 999999`) |
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

## Phase 8B decision

Proceed to a separately scoped Phase 8B production design. It must remain capability-gated, preserve competing-source
isolation and confirm the resulting position asynchronously from timeline events rather than relying on API acceptance
or the immediate post-request observation alone.
