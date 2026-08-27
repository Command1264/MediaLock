# Phase 16C Browser Session Lock candidate

## Scope and status

This candidate is the first production Browser Adapter vertical slice. It adds runtime-only Session Lock for one
explicitly authorized top-level HTTPS Page Binding, with Play, Pause, Toggle Play／Pause and bounded absolute Seek. It
includes a minimal target-detail／lock／revoke surface in the desktop UI and an unpacked Chromium Extension candidate.

The candidate is not a packaged or released browser integration. Browser App Lock, Priority Rules, Windows Auto,
settings persistence／migration, nested frames, DRM／Canvas／private players, Next／Previous／Stop and Extension-store
distribution remain out of scope.

## Candidate paths

- Unpacked Extension: `src/MediaLock.Browser/Extension`
- Register current-user development Host: `src/MediaLock.Browser/Register-BrowserIntegrationCandidate.ps1`
- Unregister exact owned registration: `src/MediaLock.Browser/Unregister-BrowserIntegrationCandidate.ps1`
- Native Host name: `com.command1264.medialock.browser`
- Extension ID: `kggfkkiifnclhhmibdglkbdfbacakemn`

Registration outputs a content-addressed ignored Host under `artifacts/browser-integration-candidate`, reports the
exact Extension path, and uses one Chrome-compatible current-user registration shared by Chrome and Brave. It does
not install, enable or update the Extension.

## Automated gate

Run from the repository root:

```powershell
dotnet test .\MediaLock.sln -c Release
pwsh -NoProfile -File .\tests\MediaLock.Browser.Tests\NativeMessagingRegistration.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass `
    -File .\tests\MediaLock.Browser.Tests\NativeMessagingRegistration.Tests.ps1
node --test .\src\MediaLock.Browser\Extension\tests\*.test.mjs
Get-ChildItem .\src\MediaLock.Browser\Extension -File -Include *.js,*.mjs |
    ForEach-Object { node --check $_.FullName }
dotnet build .\MediaLock.sln -c Release
dotnet format .\MediaLock.sln --verify-no-changes --no-restore
git diff --check
```

The deterministic matrix covers provider absence, provider-specific one-shot dispatch, exact target loss／return,
two same-title competitors, capability and Seek bounds, exact duplicate correlation, permission revocation, strict
framing／identity／configuration, Extension protocol sequences, provider-neutral physical-key capture and
ownership-safe registration. Browser Toggle resolves the exact media element's live paused state and invokes exactly
one Play or Pause; it never retries an unknown result.

Browser-specific regression tests additionally require same-tab reauthorization to remove the prior binding before
publishing its replacement, any tab reload to remove temporary and exact-site targets without automatic rebind,
stale document observations to be ignored, page-originated Play／Pause to refresh the desktop snapshot, and non-1×
playback rate to reach WPF timeline interpolation. Native port disconnect handling must consume Chromium's
`runtime.lastError` and must not disconnect the already-closed port again.
ViewModel coverage requires Browser Play to be disabled while Playing, Pause disabled while Paused and Toggle to
remain enabled in both states.
The Application gate also proves that the runtime-only Browser lock never enters the GSMTC runtime-state repository,
whose Windows adapter rejects an invalid Session Lock document before writing.

## Manual candidate matrix

All rows use a long top-level standards-based video (Nuevo Big Buck Bunny is the reference), a simultaneously playing
YouTube Music competitor, and the exact candidate paths reported by the registration script. Record browser version,
Media Lock commit, authorization scope, complete UI／popup status, observed action count, competitor isolation and any
delay／duplicate／error.

1. **No Extension lane:** Extension disabled before startup; Media Lock starts with GSMTC targets, no installation
   prompt and no error. Lock YouTube Music and verify one Pause／Play while Nuevo remains unaffected.
2. **Temporary Page Binding:** authorize Nuevo once, select the Browser page target in Media Lock, lock it, then verify
   exactly one Pause, Play, UI Toggle, physical-key Toggle and in-range Seek while YouTube Music remains unaffected.
3. **Exact target loss:** reload／navigate the locked page; every Browser target for that tab must disappear and no
   temporary or exact-site binding may be rebuilt automatically. Media Lock must show Recovering／Unavailable, its
   controls must fail closed and neither Nuevo's replacement nor YouTube Music may change. Explicit reauthorization
   creates a new identity and never silently satisfies the old lock.
4. **Permission revocation:** authorize exact-site, lock the page, use **Revoke access** in Media Lock, then verify the
   target disappears and later commands do not fall through to any GSMTC target.
5. **Disconnect／reconnect:** close or disable the Extension while the page target is locked; no competitor changes.
   Re-enable it, explicitly authorize again when continuity cannot be proven, relock and verify one Play／Pause.
6. **Brave presentation rule:** with the Extension target present, the exact Browser page appears separately. An
   uncorrelated Brave GSMTC Session remains visible; only an authoritative exact correlation may suppress its named
   duplicate. Media Lock never hides all Brave GSMTC Sessions by executable, title, URL or metadata similarity.

Manual results apply only to the exact unpacked Extension and Host candidate. They do not qualify an installer,
Extension-store package or future commit.

## 2026-08-28 pre-fix manual findings

The no-Extension lane passed with two distinguishable Brave GSMTC Sessions and exact YouTube Music Pause／Play
isolation. Temporary Browser Session Lock then passed one Pause, Play and in-range Seek against Nuevo while YouTube
Music remained unaffected. Reload removed the temporary target, preserved the unavailable locked identity and
disabled commands without falling through. Explicit reauthorization correctly required a new lock.

That run and the corrective restarts exposed eight candidate blockers before the remaining lanes:

- authorizing the same tab repeatedly accumulated old opaque bindings in the desktop catalog;
- page-originated Pause was not republished, leaving Browser playback state stale;
- non-1× playback omitted its rate, so WPF interpolated at 1×;
- Browser catalog／command updates attempted to persist the runtime-only direct lock through the GSMTC state schema,
  producing `SessionLock runtime state requires a Locked Target.` and leaving an invalid `state.json`; and
- after safe startup fallback, the already-selected Windows Auto action remained disabled while a stale startup lock
  choice remained durable, and dismissing its warning allowed ordinary state refreshes to display it again; and
- the Browser target omitted Toggle Play／Pause, leaving the UI toggle disabled and causing the provider-neutral
  physical Play／Pause key to pass through instead of routing to the exact Page Binding; and
- Extension reload／Host disconnect left Chromium's Native Messaging `runtime.lastError` unchecked and attempted to
  disconnect the already-closed port again, polluting the Extension error surface; and
- Browser Play／Pause buttons ignored the live playback state, so both remained enabled even when one explicit action
  was already satisfied.

The corrective implementation now has automated regression coverage for all eight findings and the stricter
reload-removes-all-bindings policy. Manual validation must restart from a rebuilt Extension／desktop candidate; these
pre-fix observations do not qualify the corrective commit.
