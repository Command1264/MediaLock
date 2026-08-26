# Phase 16A browser-direct Probe evidence

Date: 2026-08-26

## Environment

| Field | Observed value |
| --- | --- |
| Branch | `codex/feat/phase-16a-browser-direct-probe` |
| Initial Probe implementation commit | `ca58e5c` |
| Final hardening implementation commit | `52200f8` |
| Probe status Copy UI commit | `5c2e69a` |
| Windows | Windows 11 Pro 25H2, build `26200.9168`, 64-bit |
| Chrome | `151.0.7922.174` |
| Brave | `151.1.93.138` |
| Extension ID | `kggfkkiifnclhhmibdglkbdfbacakemn` |
| Native Host registration | Current-user Chrome-compatible registry shared by Chrome／Brave; exact Probe manifest matched |
| Authorized target origin | `https://music.youtube.com` |
| Competing／disallowed origin | Non-YouTube HTTPS page; exact URL intentionally not recorded |
| Signature／distribution | Unsigned, unpacked disposable Probe |

The evidence intentionally omits page／media URL queries, media title, artist, profile path and complete protocol
payloads. Acceptance means both the Popup result and an independently observed `HTMLMediaElement` state agreed unless
the row explicitly says the user supplied the visible result.

Revision ownership is intentionally split rather than assigning every observation to the latest document commit:

- initial fixed-site Chrome／Brave／PWA commands and isolation used `ca58e5c`;
- shared Chrome-compatible registration migration used `e505dfb`;
- five-second cold-start negotiation and the no-Extension fallback observation used `920b997`;
- stale-document races used `fb43393`;
- `e9e799b` owns the closed-tab, full-lifecycle timeout, initial sender-tab URL and content-addressed registration
  hardening. Its affected live rows remain pending below until rerun against this exact implementation revision.
- `7691dbf` queries the authoritative current tab URL because Chrome 151 and Brave do not always populate the
  optional `sender.tab.url` field during registration;
- `5ac5c24` exposes only bounded registration failure categories so live compatibility failures remain diagnosable;
- `52200f8` treats browser-owned `documentId` as a bounded opaque identifier instead of imposing UUID syntax. This
  follows live Brave rejection `registration-document-id-rejected`; the affected Chrome／Brave cross-browser
  isolation rows passed after both extensions and the Native Host were reloaded from this revision.

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
| Chrome YouTube Music Pause while ordinary Chrome YouTube also plays | Accepted once | YouTube Music paused; ordinary YouTube continued to approximately 116.19 seconds | Pass, exact-page isolation |
| Ordinary Chrome YouTube Pause while YouTube Music is paused | Accepted once | Ordinary YouTube paused at approximately 134.40 seconds; YouTube Music remained unchanged | Pass, reverse isolation |
| Three Pause／immediate Ctrl+R stale-document races | `target-unavailable` in all three rounds | Reloaded YouTube Music never received the old command; ordinary YouTube was unchanged; at most one media change per round | Pass, stale document rejected |
| Chrome YouTube Music Pause while ordinary Brave YouTube also plays, after opaque document-ID fix | Accepted once | Chrome YouTube Music paused once; ordinary Brave YouTube continued playing | Pass, cross-browser exact-page isolation |
| Pause from the loopback iframe harness while its cross-origin YouTube iframe plays | `target-unavailable` | Embedded video continued playing; no other media source changed | Pass, nested frame rejected without fallback |
| Pause with a 3000 ms Host command delay, then close the target YouTube Music tab | Popup closed with its tab before a visible result | After more than 5 seconds, ordinary Chrome YouTube continued playing, Brave media was unchanged and no duplicate occurred | Pass, closed document did not fall through to another target |
| Pause with a 6000 ms Host command delay against the five-second command deadline | `outcome-unknown` at approximately 5 seconds | After more than 8 seconds, YouTube Music continued playing; ordinary Chrome YouTube and Brave were unchanged; no late retry or duplicate occurred | Pass, full-lifecycle timeout failed closed |
| Final zero-delay Pause smoke after restoring the shared Chrome／Brave registration | Accepted once in each browser | Each selected target paused immediately; every competing source remained unchanged; no delayed or duplicate switch occurred | Pass, production-default Probe timing restored |

After reloading the `5c2e69a` Popup in Chrome, its Copy button copied the complete accepted status exactly, displayed
`Copied.` transiently and cleared that feedback automatically. No permission confirmation, error or crash appeared.

The Host registration still matched the Probe-owned manifest after the forced process stop. Reconnection required an
explicit Extension reload in this first slice; Phase 16B requires user-triggered, bounded lazy reconnect rather than an
unbounded background retry loop.

## Brave Gate A observations

| Scenario | Popup result | Observed media result | Verdict |
| --- | --- | --- | --- |
| Ordinary Brave YouTube Pause | Accepted once | Ordinary YouTube paused | Pass |
| Ordinary Brave YouTube Play | Accepted once | Ordinary YouTube played | Pass |
| Ordinary Brave YouTube Seek to 80 seconds | Accepted once | Jumped to approximately 80 seconds | Pass |
| Brave YouTube Music PWA Pause／Play | Accepted once for each command | PWA paused, then played | Pass, Extension available in installed PWA |
| PWA Pause while ordinary Brave YouTube also plays | Accepted once | PWA paused; ordinary YouTube continued | Pass, exact-page isolation |
| Ordinary Brave YouTube Pause while PWA also plays | Accepted once | Ordinary YouTube paused; PWA continued | Pass, reverse isolation |
| Reload PWA document, then Pause | Accepted once | PWA paused; ordinary YouTube unchanged | Pass |
| Force-stop every exact disposable Host, then PWA Play | `native-host-unavailable` | PWA remained paused; ordinary YouTube unchanged | Pass, fail closed |
| Reload Extension and PWA, then Play | Accepted once | PWA played; ordinary YouTube unchanged | Pass |
| Three PWA Pause／immediate Ctrl+R stale-document races | `target-unavailable` in all three rounds | Reloaded PWA never received the old command; ordinary Brave YouTube was unchanged; at most one media change per round | Pass, stale document rejected |
| Ordinary Brave YouTube Pause while Chrome YouTube Music also plays, after opaque document-ID fix | Accepted once | Ordinary Brave YouTube paused once; Chrome YouTube Music continued playing | Pass, cross-browser exact-page isolation |

After reloading the `5c2e69a` Popup in Brave, its Copy button copied the complete accepted status exactly, displayed
`Copied.` transiently and cleared that feedback automatically. No permission confirmation, error or crash appeared.

Initial registration exposed a false browser-specific assumption: the old Brave registry value matched what the
script wrote, but `brave.exe` launched the Host referenced by the Chrome-compatible registry instead. The process
chain was Probe Host → `cmd.exe` → `brave.exe`, proving the browser owner independently of the identical Extension
origin. The corrected contract uses one shared manifest／registration for both browsers and tests legacy-owned
migration plus foreign-value preservation at the public Register／Unregister seam.

After migrating to that shared registration, the first PWA command immediately following an Extension reload returned
`native-host-unavailable`; the next command was accepted once. Process ancestry showed separate shared-manifest Host
instances owned by `chrome.exe` and `brave.exe`, so registration was healthy. Inspection identified an initial
handshake race: the Popup could submit before `helloAck`. The corrected Extension initially waited up to 1.5 seconds
before the first dispatch, fails closed on timeout／disconnect and still never retries a command after posting it to
the Host.
After reloading the corrected Extension and PWA, the first Pause was accepted once, paused only the PWA and left
ordinary Brave YouTube unchanged. The bounded pre-dispatch wait therefore passed its reload-only first-click check.

A later complete Brave process restart provided a slower cold-start boundary: the first PWA Pause exceeded the
1.5-second readiness limit and failed closed, while the second Pause was accepted once after the same Brave-owned Host
finished negotiation. The bounded pre-dispatch limit is therefore 5 seconds for cold startup; live confirmation after
another complete Brave restart accepted the first Pause once, paused only the PWA and left ordinary Brave YouTube
unchanged. A complete Chrome process restart with the same 5-second build also preserved the Extension and accepted
the first YouTube Music Pause once without changing ordinary Chrome YouTube.

Invoking Pause from an active ordinary HTTPS page in Brave returned `target-unavailable`; ordinary Brave YouTube
continued playing and the already-paused PWA remained unchanged. This proves current-active-page fail-closed behavior,
not persisted closed-target Recovery: Phase 16A has no Page Binding that can name a previously closed tab.

## Disabled-Extension compatibility lane

Both unpacked Extensions were disabled while retaining the shared registration. The Probe Host count reached zero,
then installed stable Media Lock `0.3.0` was started without an Extension prompt or browser-integration dependency.
With Session Lock targeting Brave YouTube Music PWA and ordinary Brave YouTube in the foreground:

- the first physical Play/Pause paused the PWA while ordinary YouTube continued;
- the second physical Play/Pause resumed the PWA exactly once while ordinary YouTube remained unchanged;
- refreshing the PWA entered Recovering, reacquired the same PWA and routed the next physical Play/Pause exactly once;
- no Unavailable terminal state, error or crash occurred.

This passes the disabled-Extension GSMTC fallback observation. It does not prove the separately required clean Chrome
profile／never-installed Extension lane, nor imply that direct page identity or commands remain available after the
Extension is disabled; those capabilities are intentionally absent.

A separate Chrome Guest session, which had no Phase 16A Extension, published a GSMTC Session that stable Media Lock
`0.3.0` displayed as browser-level `Chrome`. Session Lock routed two physical Play/Pause presses to the Guest YouTube
Music source exactly once each: the first paused and the second resumed playback. No Extension installation prompt,
error or crash appeared. Browser-level naming is expected in this compatibility lane because page／profile identity
is intentionally unavailable without the Extension. This passes the clean-profile／never-installed Extension Gate.

## Remaining-boundary automated evidence

The final Gate A hardening adds three deterministic seams without treating them as substitutes for live-browser
observations:

- closed-tab dispatch keeps the browser-owned `documentId` on `tabs.sendMessage`; a missing／closed tab is mapped to
  `target-unavailable` and cannot fall through to another tab;
- a claimed request retains its original five-second deadline through browser dispatch. Timeout resolves the Popup
  once as `outcome-unknown`, disconnects the protocol session and rejects any late Host result instead of retrying;
- nested frames remain excluded twice: the declarative content script has `all_frames: false`, and sender registration
  independently rejects every nonzero `frameId`.
- the Native Host test configuration accepts only a 0–10000 millisecond command-response delay, never delays
  `helloAck`, and includes the exact delay in its content-addressed registration identity. Cache reuse rejects a
  mismatched configuration instead of overwriting an active Host.

These seams pass in the dependency-free Extension suite. Live iframe, closed-tab and delayed-Host timeout observations
also passed against the final hardening revision without controlling a competing source.

## Current conclusion

Chrome and Brave fixed-site Play, Pause, Seek, same-browser and cross-browser exact-page isolation, document reload,
Host-loss safety and explicit reconnect pass the manual slices. Both browsers passed full process restart with a
first command, Brave passed active non-target-page isolation, Chrome passed a separate disallowed-origin check, and
the no-Extension stable GSMTC fallback observation passed. Phase 16A Gate A is complete. Generic web media and
page-level persisted identity remain explicitly outside this fixed-site Probe and belong to Phase 16B.

The code-review hardening candidate adds automated coverage for browser-owned `documentId` replacement and stale
binding rejection; a Host／Extension fixed-vector connection ID derived from two nonces, browser family and negotiated
capabilities plus per-command capability enforcement; a 64-command pending ceiling; a single post-first-byte frame
completion timeout; finite duration／`seekable`-range Seek checks; and fail-closed closed-tab／full-lifecycle command
deadline seams. The Probe status Copy control additionally has exact-write, fail-closed and two-second transient
feedback coverage, while its manifest contract permits `clipboardWrite` but explicitly rejects `clipboardRead`.
The complete Extension suite contains 30 passing tests and the complete .NET solution contains 427 passing tests.
Manual Chrome／Brave stale-document evidence
passed three rounds per browser: every old command was rejected as `target-unavailable`, no replacement document
received the old command, every round made at most one media change and each competing ordinary YouTube source was
unchanged.
