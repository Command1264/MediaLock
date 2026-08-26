# Phase 16B generic web media Adapter evidence

Date: 2026-08-27

## Slice under test

The first disposable slice covers one user-invoked temporary `activeTab` grant on an HTTPS top-level page. It injects
the generic Adapter only after the gesture, derives the document generation from Chrome's `InjectionResult`, issues an
opaque Page Binding and binds exactly one compatible `HTMLMediaElement`. Pause continues through the Phase 16 Native
Messaging Host and exact-document dispatch.

Persistent exact-site permission, Play, Seek, frame selection, production routing／persistence and packaging are not
claimed by this slice.

## Automated evidence

- Manifest requires `activeTab` and `scripting`, keeps fixed YouTube host permissions, declares only optional HTTPS
  host access and contains no required or optional `<all_urls>` permission.
- Browser Authorization tests cover a user-invoked HTTPS page, browser-owned top-frame document identity and invalid
  URL rejection before injection.
- Browser Media Target Registry tests cover exact Page Binding／Endpoint composition, per-Endpoint capability
  enforcement and bounded page-error normalization.
- Generic Adapter tests cover one exact Pause and ambiguous multi-element rejection.
- Extension／Host protocol tests cover the generic HTTPS target schema while retaining fixed-site allowlist behavior.
- Windows PowerShell 5.1 Native Messaging registration contract passes.
- Complete solution: 428 tests passed.
- Release build: zero warnings and zero errors.

## Manual Gate B1

Pending against an exact committed revision:

1. Load the unpacked Phase 16B Extension in Chrome and confirm the fixed Extension ID.
2. Open an HTTPS page containing exactly one ordinary `<video>` or `<audio>` and start playback.
3. Open the Probe, choose `Authorize this page`, and require the temporary authorization success status.
4. Press Pause once and verify only that media element pauses once.
5. Navigate or reload the page and verify the old binding cannot command the replacement document until the user
   authorizes it again.
6. Disable the Extension and verify Media Lock's existing GSMTC path remains available without a prompt or crash.

