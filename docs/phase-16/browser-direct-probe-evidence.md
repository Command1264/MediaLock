# Phase 16A browser-direct Probe evidence

Date: 2026-08-26

## Environment

| Field | Observed value |
| --- | --- |
| Branch | `codex/feat/phase-16a-browser-direct-probe` |
| Probe implementation commit | `ca58e5c` |
| Windows | Windows 11 Pro 25H2, build `26200.9168`, 64-bit |
| Chrome | `151.0.7922.174` |
| Extension ID | `kggfkkiifnclhhmibdglkbdfbacakemn` |
| Native Host registration | Current-user Chrome registry; exact Probe manifest matched |
| Authorized target origin | `https://music.youtube.com` |
| Competing／disallowed origin | Non-YouTube HTTPS page; exact URL intentionally not recorded |
| Signature／distribution | Unsigned, unpacked disposable Probe |

The evidence intentionally omits page／media URL queries, media title, artist, profile path and complete protocol
payloads. Acceptance means both the Popup result and an independently observed `HTMLMediaElement` state agreed unless
the row explicitly says the user supplied the visible result.

## Chrome Gate A observations

| Scenario | Popup result | Observed media result | Verdict |
| --- | --- | --- | --- |
| First command against a page opened before Extension load | `target-unavailable` | No media change | Expected declarative content-script lifecycle |
| Reload target page, then Pause | Accepted once | Paused at approximately 17.87／160.98 seconds | Pass |
| Play after Pause | Accepted once | Playing; position advanced to approximately 45.96 seconds | Pass |
| Seek to 80 seconds | Accepted once | User observed jump to approximately 80 seconds; later observation advanced normally | Pass |
| Invoke Pause from a disallowed active page | `target-unavailable` | Authorized YouTube Music continued playing | Pass, fail closed |
| Reload target document, then Pause | Accepted once | New document paused at approximately 10.39 seconds | Pass |
| Force-stop the exact disposable Native Host, then Play | `native-host-unavailable` | Target remained paused; no fallback／retry | Pass, fail closed |
| Reload Extension and target page, then Play | Accepted once | Playing at approximately 26.77 seconds; one new Host process | Pass |

The Host registration still matched the Probe-owned manifest after the forced process stop. Reconnection required an
explicit Extension reload in this first slice; Phase 16B requires user-triggered, bounded lazy reconnect rather than an
unbounded background retry loop.

## Current conclusion

Chrome fixed-site Play, Pause, Seek, origin isolation, document reload, Host-loss safety and explicit reconnect pass
the first manual slice. This is **not** complete Phase 16A Gate A evidence. It does not yet prove:

- ordinary YouTube competing-source isolation in the same browser;
- stale iframe, closed-tab or timeout behavior at the live browser seam;
- full Chrome process restart and target reacquisition;
- ordinary Brave registration／control;
- installed Brave YouTube Music PWA Extension availability／control;
- generic web media or page-level persisted identity planned for Phase 16B.

Automated protocol tests cover malformed／oversized frames, exact Extension origin, stale session, sequence and request
replay, target origin／frame rejection, strict schema and command allowlists. Manual evidence remains authoritative for
actual browser media movement and duplicate count.
