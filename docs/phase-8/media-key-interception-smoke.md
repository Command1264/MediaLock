# Phase 8C production global media-key interception

## Environment

| Item | Value |
| --- | --- |
| Input device | ASUS ROG STRIX FLARE mechanical keyboard |
| Media sources | Brave YouTube Music PWA and ordinary Brave YouTube |
| Backend | `WH_KEYBOARD_LL`, ordinary-user process |
| Windows | Windows 11 Pro 25H2, build 26200.9168, 64-bit |
| Branch | `codex/feat/phase-8c-media-key-interception` |

## Automated evidence

- Virtual-key mapping covers Play/Pause, Previous, Next and Stop.
- One accepted press emits one command; repeats and matching Key-up reuse its consume decision.
- Disabled interception, unsupported capability, Recovering/Unavailable lock state and a full queue pass through.
- The queued intent retains the capture-time Session Key; a changed Active Target is skipped rather than controlled.
- Dispatch faults are surfaced without terminating the input worker.
- Settings schema v1-v5 migrates to schema v6 with interception enabled; Settings can disable and persist it.

## Hardware-assisted matrix

Before starting, play both sources, configure YouTube Music above Brave in Priority Rules, activate Priority Rules,
and make ordinary YouTube the Windows Current Session. Each row must record YouTube Music, ordinary YouTube and the
latest `route.completed` diagnostic separately.

| Check | Expected | Result |
| --- | --- | --- |
| Play/Pause once | YouTube Music changes once; ordinary YouTube unchanged | Pass |
| Next once | YouTube Music changes once; ordinary YouTube unchanged | Pass; one accepted input and one routed command |
| Previous near track start | YouTube Music changes to the previous track once | Pass |
| Previous after the player restart threshold | The current track returns to zero once instead of changing track | Pass |
| Stop once | Supported target stops once; otherwise the key passes through | Pass; YouTube Music advertised and accepted Stop |
| Play/Pause long press | One route for one physical press | Pass; one `input.accepted` and one route |
| Six rapid Play/Pause presses | Six routes with no duplicates | Pass; six inputs and six routes |
| Focus ordinary YouTube | Priority Target still receives the key once | Pass; ordinary YouTube remained unchanged |
| App Lock and Session Lock versus foreground | Locked YouTube receives the key while YouTube Music has focus | Pass in both modes |
| Disable interception in Settings | Physical key follows Windows Current Session; no Media Lock route | Pass; ordinary YouTube changed and no input was accepted |
| Re-enable interception | Priority Target receives the next key without restart | Pass; YouTube Music changed and ordinary YouTube remained unchanged |
| YouTube Music Recovery | Key passes through while no routable locked target exists; no Media Lock competing-target route | Pass; no input accepted during Recovering, then Locked Session resumed |
| Lock/unlock | Hook remains functional and routes once after unlock | Pass; restored Session Key routed with no Hook fault |
| Sleep/resume | Hook remains functional and routes once after resume | Pass across three deliberate cycles; the first wake immediately re-suspended once at the Windows lifecycle boundary, while the later cycles completed normally |
| Exit | No Media Lock process remains and Windows handles media keys | Pass; process count zero |
| Cold restart | Schema v6, enabled interception and Session Lock restore before the first key | Pass; one process, Hook enabled, Locked, and physical Play/Pause routed only to YouTube Music |

Any consumed input that changes ordinary YouTube, any duplicate route, or any key consumed without a valid target is
a blocker. Touch input is unrelated to this phase.

## Recorded diagnostics

- The controlled Play/Pause group produced exactly eight `input.accepted` and eight `route.completed` events: one
  normal press, one three-second long press and six rapid presses. Every decision was `Routed` to one Priority Target.
- The transport group produced `Next` once, `Previous` twice and `Stop` once. Track changes reconstructed the GSMTC
  Session (`gsmtc-5`, `gsmtc-8`, `gsmtc-11`), but every accepted input and its route used the same capture-time key.
- Session Lock Recovery produced no accepted input while the target was absent. Post-Recovery inputs routed only to
  the resolved successor.
- Settings ended at schema v6 with `interceptMediaKeys: true`. Cold restart restored `SessionLock`/`Locked`, started
  one Hook and routed the first physical input to the Locked Target.
- No `input.hook.start_failed`, `input.hook.faulted`, skipped route, failed route, crash or duplicate was observed.
