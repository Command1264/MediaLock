# Phase 16A browser-direct Probe plan

Date: 2026-08-26

## Decision

Phase 16A is a disposable Chrome／Brave Extension plus Native Messaging Probe. It must prove exact-tab direct control,
identity, Recovery and exactly-once behavior before any browser target enters the production Router. The published
`0.3.0` application, installer, GSMTC Adapter and retained `release/0.3` baseline remain unchanged.

The Probe does **not** add custom asymmetric encryption. Native Messaging uses a browser-created parent／child stdio
channel rather than a network listener, so ordinary network MITM is not the primary threat. The relevant threats are
an unauthorized Extension／page, confused-deputy commands, stale or replayed messages, registration／host replacement
by the same Windows user and unsafe fallback. The controls and remaining unsigned-Prototype limit are documented in
[Phase 16 Native Messaging security boundary](../research/phase-16-native-messaging-security-boundary.md).

## Module shape

```text
YouTube／YouTube Music top-level document
        ↕ fixed media-element actions
Extension service worker
        ↕ browser-owned document ID + validated tab／frame／origin + fixed Extension ID
Native Messaging protocol Module
        ↕ bounded framed JSON + connection／sequence／request correlation
Disposable Native Host
        ↕ future neutral browser-target seam
Production Router (not connected in Phase 16A first slice)
```

The protocol Module is deliberately deep: callers do not learn framing, Extension origin syntax, JSON schema,
replay bookkeeping or site allowlists. The production seam remains deferred until Chrome, Brave and the installed
Brave YouTube Music PWA prove that a second Adapter is real rather than hypothetical.

The future production composition treats this Adapter as optional capability discovery. It must be possible to omit
the browser Adapter entirely and construct the same Router graph shipped in `0.3.0`; no Core state or existing GSMTC
identity may require an Extension-derived field. An installed Extension may enrich a supported target with exact
browser identity and direct commands, but it must not replace or mutate the GSMTC representation used by unsupported
targets.

## First slice

The first slice provides:

- a fixed unpacked-Extension ID, `kggfkkiifnclhhmibdglkbdfbacakemn`, derived from the checked-in public manifest
  key;
- exact `allowed_origins` admission and a second exact launch-origin check in the Native Host;
- 64 KiB inbound／outbound Native Messaging limits before payload allocation, plus one completion deadline after
  the first byte of a frame arrives;
- strict protocol version, two fresh nonces, derived connection ID, browser family／capability negotiation, monotonic
  sequence, request UUID, 64-command pending limit, bounded dedupe cache, browser-owned document ID, target origin,
  top-frame and command allowlist validation;
- a Popup whose Play／Pause／Seek request makes a complete Extension → Host → Extension → content-script round trip;
- a 5-second bounded wait for the initial Host handshake before the first command is dispatched;
- one result for one pending request, with no mutating-command retry after timeout;
- `play()` Promise observation, Seek restricted to finite duration and finite browser-reported `seekable` ranges,
  and explicit rejection when the media element or target is unavailable;
- one Chrome-compatible current-user registration shared by Chrome and Brave, plus ownership-safe unregister scripts.

Only `play`, `pause` and `seek` are enabled. `toggle`, `next`, `previous` and `stop` remain unavailable until a later
site-Adapter slice proves exact behavior without brittle or ambiguous DOM guesses.

## Security invariants

- The Extension has only `nativeMessaging`, `tabs` and exact YouTube／YouTube Music host permissions. It has no
  `<all_urls>`, `externally_connectable`, remote code or arbitrary script command.
- Only the Extension Popup may originate a Probe command. Page payload cannot choose a tab, frame or origin.
- The service worker registers only top-level active-document metadata supplied by Chrome／Brave, carries that
  browser-owned `documentId` through the Host, and dispatches with `tabs.sendMessage(..., { documentId })`; the
  content script independently rechecks its registered document, current origin and fixed action.
- Unknown fields, schema versions, connection IDs, nonces, capabilities, sequences, request IDs, documents, origins,
  frames and commands fail closed.
- Every command must be present in the capabilities negotiated for that exact connection; membership in the broader
  Probe command allowlist is not sufficient.
- A Native Host disconnect, command timeout or target mismatch reports failure／outcome unknown and does not reroute
  to Windows Current Session.
- Initial handshake waiting occurs only before dispatch. A command already posted to the Host is never retried.
- `stdout` contains only framed protocol messages; diagnostics use `stderr`.
- Same-user replacement of the unsigned HKCU registration, manifest, host or unpacked Extension is an explicit
  Prototype limitation. Custom encryption would not repair a replaced endpoint or make the unsigned files trusted.

## Automated gate

```powershell
dotnet test '.\tests\Phase16A.BrowserDirectProbe.Tests\Phase16A.BrowserDirectProbe.Tests.csproj' `
    --configuration Release

& powershell.exe -NoProfile -ExecutionPolicy Bypass `
    -File '.\tests\Phase16A.BrowserDirectProbe.Tests\NativeMessagingRegistration.Tests.ps1'

node --test '.\experiments\Phase16A.BrowserDirectProbe\extension\tests\*.test.mjs'
node --check '.\experiments\Phase16A.BrowserDirectProbe\extension\service-worker.mjs'
node --check '.\experiments\Phase16A.BrowserDirectProbe\extension\content-script.js'
node --check '.\experiments\Phase16A.BrowserDirectProbe\extension\media-policy.js'
node --check '.\experiments\Phase16A.BrowserDirectProbe\extension\popup.js'
```

The complete repository gate remains required before review. Prototype projects must not enter release packaging or
change the `MediaLock.exe` payload.

## Registration and loading

Chrome and Brave on Windows consume the same Chrome-compatible Native Messaging registration in this tested
environment. Both `-Browser` values therefore publish and report the same shared manifest／registry path; they do not
create independent Host identities. The parameter records the browser under test rather than selecting a different
registry namespace.

```powershell
$probeRoot = '.\experiments\Phase16A.BrowserDirectProbe'

& "$probeRoot\Register-Phase16AProbe.ps1" -Browser Chrome
# Open chrome://extensions, enable Developer mode, then Load unpacked from the reported ExtensionRoot.
# Reload every already-open YouTube／YouTube Music test tab once so Chrome injects the declarative content script.

& "$probeRoot\Register-Phase16AProbe.ps1" -Browser Brave
# The second registration must report the same NativeHostManifest and RegistryPath.

& "$probeRoot\Unregister-Phase16AProbe.ps1" -Browser Brave
```

For Brave, load the Extension from `brave://extensions`. The shared Windows lookup is an empirical compatibility
result and must be recorded with the exact Brave version and the launched Host's browser parent process. A Brave
failure does not authorize adding a wildcard origin, a second Extension ID or a localhost listener. Register safely
migrates only the Probe's exact legacy browser-specific manifest paths; it rejects any foreign value. Unregister
removes only the exact shared or legacy Probe-owned value, so calling it for either browser disconnects both browser
test lanes. Both scripts also remove the obsolete Brave-specific key only when it still points to an exact
Probe-owned manifest; a foreign value at that location is preserved.

Phase 16A deliberately uses a declarative fixed-site content script. Loading or reloading an unpacked Extension does
not retrofit that script into documents that were already open; those stale documents reject with
`target-unavailable` until refreshed. Record this lifecycle result rather than adding an unsafe command retry. Phase
16B's separately gated `activeTab + scripting` design owns on-demand injection for the generic Adapter.

## Manual Gate A

Record browser version, Extension ID, registration path, active page, expected command, actual media change, duplicate
count and Native Host／Extension errors for every row:

1. Chrome YouTube Music: Play, Pause and Seek while another browser media source exists.
2. Brave ordinary YouTube: Play, Pause and Seek without changing Chrome YouTube Music.
3. Installed Brave YouTube Music PWA: fixed Extension availability, command routing and ordinary Brave isolation.
4. Active disallowed page, iframe, stale tab and closed tab: command rejected without controlling another target.
5. Ctrl+R, navigation, Extension reload, Native Host disconnect and browser restart: old request cannot dispatch.
6. Timeout／disconnect: no automatic retry and no duplicate media change.
7. A clean Chrome profile without the Extension: the published `0.3.0` GSMTC workflow remains fully usable and no
   Extension installation prompt blocks Media Lock.

Gate A is not complete until the Extension can rediscover and bind the same logical target after reload without using
media title or artist as identity. Passing Gate A does not imply Chromium stopped publishing GSMTC or that Media Lock
can own Windows Current Session.

Preserve completed observations in
[Phase 16A browser-direct Probe evidence](browser-direct-probe-evidence.md); untested rows remain explicit rather than
being inferred from automated protocol coverage.

## Exit and cleanup

The unregister script removes only an exact registry value that still points to the Probe-owned manifest. It preserves
foreign or changed registrations and retains generated output for inspection. Remove the unpacked Extension from the
browser after testing; generated `artifacts/phase16a-browser-direct/` content is local, ignored evidence rather than a
release asset.
