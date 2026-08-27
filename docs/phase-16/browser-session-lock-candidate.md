# Phase 16C Browser Session Lock candidate

## Scope and status

This candidate is the first production Browser Adapter vertical slice. It adds runtime-only Session Lock for one
explicitly authorized top-level HTTPS Page Binding, with Play, Pause and bounded absolute Seek. It includes a minimal
target-detail／lock／revoke surface in the desktop UI and an unpacked Chromium Extension candidate.

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
node --test .\src\MediaLock.Browser\Extension\tests\*.test.mjs
Get-ChildItem .\src\MediaLock.Browser\Extension -File -Include *.js,*.mjs |
    ForEach-Object { node --check $_.FullName }
dotnet build .\MediaLock.sln -c Release
dotnet format .\MediaLock.sln --verify-no-changes --no-restore
git diff --check
```

The deterministic matrix covers provider absence, provider-specific one-shot dispatch, exact target loss／return,
two same-title competitors, capability and Seek bounds, exact duplicate correlation, permission revocation, strict
framing／identity／configuration, Extension protocol sequences and ownership-safe registration.

## Manual candidate matrix

All rows use a long top-level standards-based video (Nuevo Big Buck Bunny is the reference), a simultaneously playing
YouTube Music competitor, and the exact candidate paths reported by the registration script. Record browser version,
Media Lock commit, authorization scope, complete UI／popup status, observed action count, competitor isolation and any
delay／duplicate／error.

1. **No Extension lane:** Extension disabled before startup; Media Lock starts with GSMTC targets, no installation
   prompt and no error. Lock YouTube Music and verify one Pause／Play while Nuevo remains unaffected.
2. **Temporary Page Binding:** authorize Nuevo once, select the Browser page target in Media Lock, lock it, then verify
   exactly one Pause, Play and in-range Seek while YouTube Music remains unaffected.
3. **Exact target loss:** reload／navigate the locked page and issue a command before a valid successor is published;
   Media Lock must show Recovering／Unavailable and neither Nuevo's replacement nor YouTube Music may change.
4. **Permission revocation:** authorize exact-site, lock the page, use **Revoke access** in Media Lock, then verify the
   target disappears and later commands do not fall through to any GSMTC target.
5. **Disconnect／reconnect:** close or disable the Extension while the page target is locked; no competitor changes.
   Re-enable it, explicitly authorize again when continuity cannot be proven, relock and verify one Play／Pause.
6. **Brave presentation rule:** with the Extension target present, the exact Browser page appears separately. An
   uncorrelated Brave GSMTC Session remains visible; only an authoritative exact correlation may suppress its named
   duplicate. Media Lock never hides all Brave GSMTC Sessions by executable, title, URL or metadata similarity.

Manual results apply only to the exact unpacked Extension and Host candidate. They do not qualify an installer,
Extension-store package or future commit.
