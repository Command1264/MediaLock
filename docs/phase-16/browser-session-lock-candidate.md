# Phase 16C Browser Session Lock candidate

## Scope and status

This candidate is the first production Browser Adapter vertical slice. It adds runtime-only Session Lock for one
explicitly authorized top-level HTTPS Page Binding, with Play, Pause, Toggle Play／Pause and bounded absolute Seek. It
also lets the existing Keep Playing policy protect that exact Browser target. It includes a minimal
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
publishing its replacement, any tab reload to remove the old temporary or exact-site target, temporary grants never
to auto-bind, and trusted-site replacement documents to publish at most one new target without satisfying the old
lock. A reload during explicit authorization and permission loss during trusted-site binding must discard the
uncommitted target before publication. Stale document observations must be ignored, page-originated Play／Pause must
refresh the desktop snapshot, and non-1× playback rate must reach WPF timeline interpolation. Native port disconnect
handling must consume Chromium's `runtime.lastError` and must not disconnect the already-closed port again.
Popup coverage must execute the real Popup entry against a fake DOM and top-level document message contract. A live
Binding reports Authorized; a retained exact-site permission without a current Binding reports trusted-site waiting;
and neither state reports Not authorized. The status response never exposes URL, title, Binding identity or media
metadata. Closing and reopening the Popup must preserve Authorized for the same live document. Browser-locale tests
cover English fallback and Traditional Chinese, and layout coverage keeps the two actions vertically separated.
Because Chromium may replace the Popup while its native exact-site permission prompt is open, the production Service
Worker owns the permission-added continuation. A first exact HTTPS grant binds matching completed tabs without a
second Popup action; broad, wildcard, malformed, non-HTTPS and unrelated permission additions fail closed.
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
2. **Temporary Page Binding:** authorize Nuevo once, select the Browser page target in Media Lock, use the ordinary
   **Lock Session** mode action, then verify exactly one Pause, Play, UI Toggle, physical-key Toggle and in-range Seek
   while YouTube Music remains unaffected. While Nuevo is Playing, arm **Keep Playing**, pause it once from the page
   and verify one explicit Play restores only Nuevo. Trigger the configured repeated-pause override and verify it
   releases without the threshold Play. No separate Browser lock action may appear inside the source list.
3. **Exact target loss:** reload／navigate the locked page; the old Browser target must disappear immediately. A
   temporary grant creates no successor. With exact-site permission, the completed replacement document creates one
   different Browser target automatically, but Media Lock must keep the old Router identity Recovering／Unavailable,
   keep controls failed closed and never auto-select／lock the successor. Neither Nuevo's replacement nor YouTube
   Music may receive a command. If Keep Playing was armed, it remains Suspended and sends no Play to the successor or
   any GSMTC competitor. The Popup shows trusted-site waiting before the replacement binds, then Authorized.
4. **Permission revocation:** authorize exact-site, lock the page, open that Browser row's overflow menu and use
   **Revoke access**, then verify the exact target disappears and later commands do not fall through to any GSMTC
   target. Opening the menu must not change selection or routing. Reopening the Popup must show Not authorized.
5. **Disconnect／reconnect:** close or disable the Extension while the page target is locked; no competitor changes.
   Re-enable it; temporary scope requires explicit authorization, while exact-site scope auto-creates a new target on
   the next completed page load. Neither path may repair the old lock implicitly; explicitly relock and verify one
   Play／Pause.
6. **Brave presentation rule:** with the Extension target present, one expandable Brave presentation group contains
   the exact Browser page and the ordinary Brave GSMTC Session as distinct children. Installed YouTube Music remains a
   separate application group. Expanding uses the shared bounded scrollbar rather than growing over other groups or
   the transport controls. The uncorrelated GSMTC child remains visible; only an authoritative exact correlation may
   suppress its named duplicate. Browser and Windows rows keep their playback pills aligned despite the Browser-only
   overflow action. Adding an authorized Browser child preserves an already-expanded Brave group; wheel input over
   either child list moves the shared outer scrollbar. Selecting or focusing either child does not shift its geometry,
   and the themed overflow menu has no default WPF icon gutter. Grouping never hides or merges targets by title, URL or
   metadata similarity.
7. **Popup locale and layout:** reload the unpacked Extension under the browser's current UI language. English and
   Traditional Chinese must use their complete locale resources, unsupported languages must fall back to English,
   and the two authorization actions must remain vertically separated with readable wrapping. Status must distinguish
   an active Binding, a trusted site waiting for a Binding and a page with no authorization.

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
reload-removes-old-bindings policy. Manual validation must restart from a rebuilt Extension／desktop candidate; these
pre-fix observations do not qualify the corrective commit.

## 2026-08-28 local Browser Keep Playing result

The rebuilt local Release candidate ran independently under Windows Explorer on Windows build `26200`, with Brave
`151.1.93.138`, the unpacked candidate Extension and Nuevo Big Buck Bunny competing with a playing YouTube Music
Session. The source was the modified working tree based on `ff60b442fa1cf99b8d062746632080d4d28c5cf6`; therefore
this result validates the local implementation but is not exact-commit, PR, installer or release evidence.

All six focused checks passed:

1. selecting and locking the playing Nuevo Browser target enabled Keep Playing without affecting YouTube Music;
2. one page-originated Pause produced exactly one Play correction to Nuevo and restored the UI to Playing;
3. three distinct pauses inside the configured window corrected the first two, released Keep Playing on the third,
   left Nuevo paused and produced the configured release feedback;
4. reloading the armed page removed the Browser target, retained its identity, showed the localized waiting／Suspended
   state and sent no Play to the reloaded page or YouTube Music;
5. explicitly reauthorizing the reloaded page created one different `browser:` identity without adopting, selecting,
   locking or correcting it; and
6. explicitly locking that new target cleared the old suspended policy, restored Locked state and enabled Keep
   Playing again while YouTube Music remained unaffected.

No delayed or duplicate correction, warning, Extension error or crash was observed in these six checks.

## 2026-08-28 trusted-site auto-binding and localized Popup result

The rebuilt unpacked Extension passed five manual checks against Nuevo Big Buck Bunny with YouTube Music playing:

1. the Popup followed Brave's Traditional Chinese UI language, kept its authorization actions vertically separated
   without clipping and distinguished a retained site grant waiting for a live Binding;
2. reloading the trusted Nuevo site created exactly one new Browser target without another authorization gesture and
   the Popup reported Authorized;
3. a second reload removed the predecessor, created one different `browser:` identity, preserved the unavailable old
   Router identity and neither selected nor locked the successor;
4. a second same-origin tab automatically created its own distinct target, and closing that tab removed only its
   target while the first tab and YouTube Music remained unaffected; and
5. revoking site access removed the target and projected Not authorized even while the old document listener was
   still present. One-time authorization then created exactly one target, and reload removed it without automatic
   replacement.

Post-acceptance review added deterministic regression coverage for two lifecycle gaps before commit: a delayed
trusted-site binding can no longer publish after reload／tab-close invalidation, and exact discard cannot remove a
newer same-tab successor. Desktop revocation now unbinds the exact live document before registry removal, so a
temporary binding cannot remain Popup-visible as Authorized. Popup failures also map internal error identifiers to
localized actionable English／Traditional Chinese prose with stable `ML-BR-*` support codes. These corrections require
a focused manual regression rerun before the candidate can be committed or the PR description updated.

The focused rerun found one additional runtime-boundary defect: the controller accepted exact unbind, but the
content-script listener allowlist dropped `unbindGenericEndpoint`. The desktop target disappeared while reopening the
Popup still reported Authorized. A RED runtime-boundary test reproduced that exact message path; the listener now
forwards unbind and the complete Extension suite covers the corrected behavior. Brave may continue to display a
user-managed site in its **Specific sites** settings after `permissions.remove()` has revoked the Extension runtime
grant. In the observed candidate, `permissions.contains()` remained false, Popup remained Not authorized and reload
did not auto-create a target; this browser-owned allow-list presentation is not evidence of a live Media Lock grant.

The final focused manual regression completed 4／4 rows on 2026-08-28:

1. an ineligible Brave page displayed localized Traditional Chinese guidance with `ML-BR-001` and no raw internal
   `page-not-eligible` text;
2. five rapid Nuevo reloads converged to exactly one different Browser target, removed every predecessor, preserved
   the unavailable old Router identity and sent no unintended command;
3. exact-site revoke removed the target and runtime grant, kept Popup Not authorized across reload and created no
   automatic successor. Brave retained its user-managed **Specific sites** row visually, but that row remained
   inactive for Extension runtime permission and did not restore Media Lock authority; and
4. after the content-script allowlist correction, temporary revoke immediately removed the target, disabled desktop
   controls, preserved the unavailable locked identity and made the still-open document report Not authorized without
   a reload. Nuevo and YouTube Music remained unaffected, with no Extension error, warning or crash.

The revocation check initially exposed stale Popup authorization projection; the Popup now revalidates exact-site
permission for every site-scoped live-document response. The corrected run showed no duplicate target, warning,
Extension error or crash.

Final two-axis review found one remaining pre-publication race: explicit authorization did not share the tab-generation
guard, and trusted-site binding did not revalidate its permission after the asynchronous Endpoint／Native Host work.
The production paths now share one guarded coordinator that serializes binding work per tab. RED regressions prove
that reload during explicit authorization and permission loss during trusted-site binding both discard the exact
uncommitted target instead of publishing it, and that a late stale bind cannot overwrite its newer document successor.
The complete Extension suite passes 62／62 after this correction; it changes no visible interaction, so the accepted
rapid-reload and revoke rows above remain the corresponding manual evidence.

The grouped Browser row's overflow revocation was then exercised once against the locked Nuevo target. The exact
Browser target disappeared, Router retained the unavailable provider-qualified identity, transport controls failed
closed and neither Nuevo, its Brave GSMTC surface nor YouTube Music received a command. Opening a fresh Popup always
showed its former transient `Ready.` prompt, so Popup text was not treated as authorization-state evidence in that
earlier run; target removal and fail-closed desktop routing were the authoritative observations. The later
state-aware Popup checks replace that limitation. No Extension error, warning or crash appeared.
