# Phase 16C production browser integration gate

## Outcome

Promote only the proven Phase 16 browser-direct behavior into a reviewable production design while keeping the
complete `0.3.0` GSMTC path independent. Phase 16C freezes the production seam, evidence requirements and staged
implementation slices. It does not connect the disposable Probe to the production Router, change settings or ship an
Extension.

Status: gate definition accepted through PR #59 and the provider-neutral Core／Application seam is integrated.
Issue #62 implements slice 3 as a runtime-only Browser Session Lock candidate. Persistence, remaining Routing Modes,
store distribution, installed-package ownership and support claims remain separately gated by this plan,
[ADR 0006](../adr/0006-use-provider-neutral-media-targets-in-production-routing.md) and
[ADR 0007](../adr/0007-use-a-current-user-native-messaging-bridge.md).

## Entry evidence and unresolved claims

Phase 16A proved fixed-site Play, Pause and Seek with exact-page isolation on Chrome, ordinary Brave and the installed
Brave YouTube Music PWA. Phase 16B proved a temporary or exact-site Page Binding, one top-level
`HTMLMediaElement`, bounded Play／Pause／Seek, revocation, ambiguity and stale-target
failure on Chrome. The subsequent Brave compatibility-closure Gate qualified named direct MP4, cloud-hosted MP4,
ordinary streaming and unsupported-page samples. Both Probes proved that disabling or omitting the Extension leaves
the existing GSMTC path usable.

That evidence does not yet support a production compatibility claim for every row named by the Phase 16B plan:

| Row | Current evidence | Gate disposition |
| --- | --- | --- |
| Fixed YouTube／YouTube Music Adapter on Chrome, Brave and Brave PWA | Complete disposable-Probe evidence | Retain as provider-specific evidence; rerun against the production candidate. |
| Generic top-level media on Chrome | MDN single-video temporary／exact-site evidence complete | Retain as the first generic reference row. |
| Generic top-level media on Brave | Named MDN and Nuevo Brave Gate complete | Retain as disposable-Probe evidence; rerun against the production candidate. |
| Directly hosted MP4 | Covered by the named MDN top-level sample | Record the exact sample and browser version again for the production candidate. |
| Cloud-hosted MP4 | Named Internet Archive direct MP4 Brave Gate complete | Retain as disposable-Probe evidence; rerun against the production candidate. |
| Ordinary non-YouTube streaming page | Named Nuevo standards-based Brave Gate complete | Retain as disposable-Probe evidence; rerun against the production candidate. |
| Multiple elements, nested iframe and revoked permission | Chrome and Brave named fail-closed evidence complete | Preserve fail-closed behavior; nested-frame selection stays out of scope. |
| DRM, Canvas and private players | Named unsupported item-page fail-closed evidence; no DRM／Canvas／private control claim | Keep the entire class unsupported and rerun an implementation-specific named fail-closed sample before production compatibility wording. |
| No Extension installed | Stable `0.3.0` Chrome Guest and Brave installed-PWA GSMTC lanes passed | Rerun the complete relevant stable regression matrix against every production candidate. |

No generic row passes merely because its page contains an `HTMLMediaElement`; support is attached to named evidence.
Unsupported rows remain available only through GSMTC when their source publishes a Media Session.

## Production seam

The Router consumes one provider-neutral media-target interface. GSMTC and Browser are peer Adapters at that seam;
neither is represented as the other provider's identity.

The interface exposes only:

- an immutable, provider-qualified target identity;
- an immutable observation with presentation, playback, timeline and advertised capabilities;
- catalog change and target-loss observations; and
- one one-shot Media Command result that distinguishes unsupported, rejected, failed and outcome unknown.

The Browser Adapter Module hides Extension identity, browser installation／profile discovery, authorization, Native
Messaging, tab, frame, document generation and Endpoint selection. The existing GSMTC Adapter continues to hide
WinRT Session handles. Core and Application callers never receive either provider's transport handles.

A new typed identity is added rather than widening `SessionFingerprint` or placing browser fields on a Media Session.
Existing persisted GSMTC selectors retain their exact `SourceAppUserModelId` and schema meaning. A future settings
schema adds an explicit provider and selector kind; migration never guesses a Browser Media Target from an existing
browser AUMID, friendly name, title or URL.

Visible-target reconciliation requires an authoritative exact link from a present direct target to one present GSMTC
target. Only that GSMTC duplicate is suppressed. Browser executable, title, URL, origin similarity, tab order and
track metadata are never correlation evidence; uncorrelated Brave GSMTC targets remain visible and controllable.

## Provider and Recovery states

Provider absence and loss of a bound target are different state transitions:

1. **Provider absent before binding** — no Extension, denied authorization or unavailable Native Host adds no direct
   targets. Media Lock starts and remains GSMTC-only without a blocking prompt or settings migration.
2. **Bound target lost** — permission revocation, disconnect, navigation or stale Endpoint preserves the Browser Media
   Target identity and enters target-preserving Recovery／Unavailable. Session Lock never reroutes that command to a
   competing GSMTC Session or page.
3. **Reload／navigation** — browser `loading` removes every binding for that tab, including exact-site bindings. Site
   permission may remain, but no successor is created until an explicit authorization issues a new Page Binding. A
   command captured for the old Endpoint is rejected, not replayed.
4. **Browser restart** — the target stays unavailable unless the Extension proves Page Binding continuity. URL, title,
   origin similarity and tab order are never Recovery evidence by themselves.
5. **Return to GSMTC** — the user explicitly unlocks／changes Routing Mode or selects a GSMTC target. Adapter failure is
   not implicit permission to change a Locked Target.

Every mutating command is dispatched at most once. Readiness may be awaited only before dispatch; timeout or an
unknown outcome never retries after the command crosses the Browser Adapter seam.

## Routing Mode scope

The first production slice supports only Session Lock with an exact Page Binding. Other modes remain GSMTC-only until
their independent slices pass:

- App Lock persists one exact Browser Application Scope and refuses unresolved page ambiguity;
- Priority Rules use typed page- or application-scoped selectors and never collapse two pages into one browser rule;
- Windows Auto persists no Browser Media Target, but every decision still names the exact resolved page.

Temporary `activeTab` authorization may create a runtime Session Lock, but it is unavailable after Extension or
browser restart unless continuity is proved. Exact-site permission does not itself prove Page Binding continuity.

## Implementation slices

### 1. Compatibility closure and design review

- Complete or explicitly narrow every unresolved row in the table above.
- Review this plan, ADR 0006, the Native Messaging security boundary and the Extension distribution model.
- Keep the Probe projects disposable and outside production packaging.

### 2. Provider-neutral Core／Application seam

Status: implemented as the Issue #60 candidate; merge remains separately gated.

- Start with failing tests for distinct GSMTC and Browser identities, two same-title pages, capability checks,
  expected-target capture and one-shot outcomes.
- Adapt the existing GSMTC catalog and controller without changing observable routing behavior.
- Prove the no-Extension composition is behaviorally equivalent before adding a production Browser Adapter.
- Expose reconciled Media Targets while retaining an explicit GSMTC Sessions projection for the unchanged UI and
  persistence behavior.

### 3. Browser Session Lock vertical slice

Status: candidate implemented by Issue #62; automated and named manual qualification remain the acceptance gate.

- Add the production Browser Adapter Module behind the accepted seam.
- Discover only explicitly authorized targets and route Play, Pause and bounded Seek to one exact Endpoint.
- Preserve target identity through Recovery while failing closed on ambiguity, permission loss and stale Endpoint.
- Add the minimum authorization and target-detail UI needed to create, inspect and revoke a direct Session Lock.

The candidate uses an unpacked fixed-ID Chromium Extension and a current-user-only Native Messaging／named-pipe bridge.
It is intentionally runtime-only and does not enter the installed `0.3.0` payload. See the
[candidate runbook](browser-session-lock-candidate.md).

### 4. Remaining Routing Modes and persistence

- Add typed schema migration and restart behavior only after Session Lock passes.
- Implement App Lock, Priority Rules and Windows Auto separately with two-page ambiguity tests for each mode.
- Preserve the exact semantic meaning of every existing GSMTC setting and never reinterpret it during migration.

### 5. Packaging and release qualification

- Select a reviewable Extension distribution and update channel; an unpacked developer Extension is not a production
  dependency.
- Install and remove only the current-user Native Host registration owned by the exact installed package. Portable and
  installed copies must not claim or delete each other's registration.
- Run independent no-Extension and Extension-available compatibility lanes, the complete relevant `0.3.0` regression
  suite, upgrade／repair／uninstall checks and exact-artifact host／clean-Windows gates.

## Explicit non-goals

- Suppressing another application's GSMTC publication or forcing Media Lock to be Windows Current Session.
- Required `<all_urls>` permission, process injection, browser automation through simulated clicks or arbitrary page
  JavaScript.
- Nested-frame selection, DRM／Canvas／private-player control or automatic URL-based rebinding.
- Next／Previous／Stop through the Generic Adapter without separate provider-specific evidence.
- Shipping, tagging or publishing an Extension, Native Host or Media Lock release as part of this gate-definition task.

## Validation

This gate-definition change requires terminology／link review and `git diff --check`. Each later code slice additionally
runs formatting, the complete automated solution tests, Release build and focused Browser Adapter tests. A production
candidate must then pass the named manual matrix in both compatibility lanes; Probe evidence does not transfer to a
different executable, Extension revision or package digest.
