# Phase 16B generic web media Adapter evidence

Date: 2026-08-27

Implementation commits:

- Temporary authorization and Pause: `bf55b18`
- Play: `9d3d258`
- Seek, exact-site authorization and Recovery: `e07faea`

## Slice under test

The completed disposable Probe covers a user-invoked temporary `activeTab` grant or an explicit exact-HTTPS-site
grant on a top-level page. It injects the generic Adapter only after the gesture, derives the document generation from
Chrome's `InjectionResult`, issues an opaque Page Binding and binds exactly one compatible `HTMLMediaElement`. Pause,
Play and bounded Seek continue through the Phase 16 Native Messaging Host and exact-document dispatch. Async Play
waits for the browser result; every command reports a bounded result and is never retried.

An exact-site Page Binding may adopt a successor document after reload or same-origin navigation while permission is
still present. Cross-origin navigation, permission revocation, tab removal, stale documents, detached media elements
and ambiguous pages fail closed. The fixed YouTube listener ignores Generic Adapter messages so the two adapters do
not race to answer the same request.

The disposable Probe intentionally supports only the authorized top-level frame. Nested-frame selection, automatic
binding recovery after Extension／browser restart, production routing／persistence, UI discovery and packaging are not
claimed. Unsupported or inaccessible-frame pages continue to rely on the unchanged GSMTC path.

## Automated evidence

- Manifest requires `activeTab` and `scripting`, keeps fixed YouTube host permissions, declares only optional HTTPS
  host access and contains no required or optional `<all_urls>` permission.
- Browser Authorization tests cover temporary and exact-site grants, browser-owned top-frame document identity,
  same-origin successor documents, cross-origin suspension and immediate permission revocation.
- Browser Media Target Registry tests cover exact Page Binding／Endpoint composition, per-Endpoint capability
  enforcement, two same-origin pages with identical presentation, live-endpoint suspension and bounded page-error
  normalization.
- Generic Adapter tests cover one exact Pause, Play and Seek; rejected Play without retry; invalid Seek without
  mutation; detached media replacement; and ambiguous multi-element rejection.
- The content message boundary test covers keeping the Chrome response channel open until Play settles.
- A coexistence test proves the fixed YouTube listener ignores Generic Adapter messages without racing a response.
- Extension／Host protocol tests cover the generic HTTPS target schema while retaining fixed-site allowlist behavior.
- The popup permission seam proves one user gesture requests only the active page's exact HTTPS origin.
- Windows PowerShell 5.1 Native Messaging registration contract passes.
- Extension: 59 tests passed.
- Complete solution: 428 tests passed.
- Formatting verification passed without changes.
- Release build: zero warnings and zero errors.

## Manual Gate B1

Passed against implementation commit `bf55b18`:

1. Load the unpacked Phase 16B Extension in Chrome and confirm the fixed Extension ID.
2. Open an HTTPS page containing exactly one ordinary `<video>` or `<audio>` and start playback.
3. Open the Probe, choose `Authorize this page`, and require the temporary authorization success status.
4. Press Pause once and verify only that media element pauses once.
5. Navigate or reload the page and verify the old binding cannot command the replacement document until the user
   authorizes it again.
6. Disable the Extension and verify Media Lock's existing GSMTC path remains available without a prompt or crash.

Observed on Chrome with the MDN single-video sample:

- The fixed Extension ID matched.
- Temporary authorization reported one media element.
- Pause was accepted once and paused the video once without an error or crash.
- Play remained unavailable by design because the first slice advertises only the Pause capability.
- After reload, the stale binding was rejected as `target-unavailable` and the playing replacement document did not
  receive the old Pause command.
- Reauthorization bound the replacement document, after which Pause was accepted exactly once.
- With the Extension disabled, Media Lock's existing GSMTC path remained available without an error or crash.

## Manual Gate B2

Passed against Play implementation commit `9d3d258`:

1. Reload the unpacked Phase 16B Extension in Chrome.
2. Open the MDN single-video sample, start playback and authorize the page.
3. Press Pause once and verify the video pauses once.
4. Press Play once and verify the video resumes once.
5. Verify no other media source changes and no delayed or duplicate command occurs.

Observed on Chrome with the MDN single-video sample:

- Temporary authorization reported one media element.
- Pause was accepted once and paused the video once.
- Play was accepted once and resumed the video once.
- No delayed or duplicate command appeared after three seconds.
- Other media sources did not change, and no error or crash occurred.

## Final manual Gate B3

Passed on Chrome against final code candidate `fd2d530`:

1. Temporary authorization bound the MDN single-video HTTPS page. Pause and Play each executed once; Seek to four
   seconds executed once, while an out-of-range ten-second Seek returned `seek-out-of-range` without mutation. A
   concurrently playing ordinary YouTube page never changed.
2. `Always allow this site` displayed Chrome's exact-site confirmation. Reload recovered the same MDN Page Binding
   without another authorization gesture. Navigating the tab to `example.com` returned `target-unavailable`; returning
   to the authorized MDN origin recovered the binding without controlling the competitor.
3. Revoking MDN site access immediately changed Pause to `target-unavailable` while both media sources retained their
   states. A new temporary user gesture restored direct control.
4. Two top-level media elements returned `ambiguous-media-elements`; the following command returned
   `target-unavailable` and selected neither element. A W3Schools nested-iframe-only video returned
   `media-element-unavailable`, stayed playing and did not alter ordinary YouTube.
5. The fixed YouTube Adapter accepted Pause and Play once before generic authorization. After generic authorization,
   the same page again accepted each command once, proving the two listeners no longer race a response.
6. Extension reload and complete Chrome restart invalidated the temporary Page Binding. Pause returned
   `target-unavailable` until a new explicit authorization; the replacement binding then accepted Pause and Play once.
7. Force-stopping Native Host PID 40260 caused Chrome to start successor PID 29896 before the next command. The
   recovered Host accepted Pause once, with no delayed／duplicate operation or competitor change. Isolated automated
   tests retain coverage of the unavailable branch.
8. With the Phase 16B Extension disabled, the stable Media Lock GSMTC path still listed the Chrome Session and two
   physical Play／Pause presses paused and resumed it exactly once each. No install／authorization prompt, error or
   crash appeared; the Extension was re-enabled after the check.

This Gate proves the bounded top-level generic scope. A provider-specific cloud-drive player was not separately
qualified; a standard top-level `HTMLMediaElement` follows the same tested Adapter path, while a private／DRM／nested
frame implementation remains an explicitly unsupported GSMTC fallback rather than a false direct capability.
