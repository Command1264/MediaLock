# Phase 16B generic web media Adapter evidence

Date: 2026-08-27

Implementation commits:

- Temporary authorization and Pause: `bf55b18`
- Play: `9d3d258`

## Slice under test

The first disposable slice covers one user-invoked temporary `activeTab` grant on an HTTPS top-level page. It injects
the generic Adapter only after the gesture, derives the document generation from Chrome's `InjectionResult`, issues an
opaque Page Binding and binds exactly one compatible `HTMLMediaElement`. Pause and Play continue through the Phase 16
Native Messaging Host and exact-document dispatch. Play waits for the browser's asynchronous result, reports a
rejection explicitly and never retries the command.

Persistent exact-site permission, Seek, frame selection, production routing／persistence and packaging are not
claimed by this slice.

## Automated evidence

- Manifest requires `activeTab` and `scripting`, keeps fixed YouTube host permissions, declares only optional HTTPS
  host access and contains no required or optional `<all_urls>` permission.
- Browser Authorization tests cover a user-invoked HTTPS page, browser-owned top-frame document identity and invalid
  URL rejection before injection.
- Browser Media Target Registry tests cover exact Page Binding／Endpoint composition, per-Endpoint capability
  enforcement and bounded page-error normalization.
- Generic Adapter tests cover one exact Pause, one exact Play, rejected Play without retry and ambiguous
  multi-element rejection.
- The content message boundary test covers keeping the Chrome response channel open until Play settles.
- Extension／Host protocol tests cover the generic HTTPS target schema while retaining fixed-site allowlist behavior.
- Windows PowerShell 5.1 Native Messaging registration contract passes.
- Complete solution: 428 tests passed.
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
