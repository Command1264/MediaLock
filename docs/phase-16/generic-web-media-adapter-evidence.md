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

Pending against the final disposable Probe candidate. The remaining browser checks cover behavior that the isolated
JavaScript tests cannot prove against Chrome's real permission, document-generation and media-element implementation:

1. Temporary authorization: Pause, Play and Seek execute once on one HTTPS media page; another playing page is
   unchanged.
2. Exact-site authorization: the browser grants only that origin; reload recovers the same Page Binding without a
   second authorization gesture; cross-origin navigation does not inherit the binding.
3. Revocation: removing site access immediately rejects the direct command and leaves media unchanged.
4. Ambiguity and unsupported pages: zero or multiple top-level media elements fail closed without controlling another
   source; nested-frame-only media remains GSMTC-only in this Probe.
5. Lifecycle: tab close, Extension reload, browser restart and Native Host interruption never replay or silently
   redirect an old command. A new explicit binding is required where continuity cannot be proved.
6. Compatibility: the fixed YouTube Adapter still controls its exact page once, while disabling the Extension leaves
   Media Lock's existing GSMTC routing functional.
