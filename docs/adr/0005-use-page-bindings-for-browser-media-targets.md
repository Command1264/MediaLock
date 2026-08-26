# Use explicit Page Bindings for browser media targets

## Status

Accepted for the Phase 16B disposable Probe on 2026-08-27. Production persistence and packaging remain gated by
the complete compatibility matrix and a later production-integration review.

## Context

GSMTC identifies a browser media Session at browser or installed-Web-App scope, but it cannot reliably distinguish
two playable pages in the same browser. Phase 16B adds an optional direct browser path without making the Extension a
prerequisite for Media Lock. Page title, media title, URL similarity, browser executable and tab order are useful
presentation facts but are not durable routing identity.

The browser controls tab, frame, document and permission facts. A page or content script is not authoritative for
those values, and a live media element can be replaced without the logical target changing.

## Decision

The Browser Adapter Module owns three interfaces:

1. The Browser Authorization Module converts one explicit user gesture into either a temporary `activeTab` grant or
   an exact HTTPS-site grant. Denial and revocation remove direct capability immediately.
2. The Browser Media Target Registry issues an opaque Page Binding. It derives browser profile, authorized origin,
   tab, frame and document facts from browser-owned data and never reconstructs a binding from presentation data.
3. The Generic Web Media Adapter discovers bounded `HTMLMediaElement` candidates and binds exactly one explicit
   Browser Media Endpoint. Zero candidates are unavailable; multiple plausible candidates are ambiguous.

The first generic command set is Play, Pause and Seek. Dispatch names one Page Binding, current document generation,
frame and Endpoint, executes at most once and fails closed when any identity or permission fact is stale.

Reload and same-origin navigation invalidate the live Endpoint. A successor may be resolved only under the same Page
Binding and authorized scope. Cross-origin navigation suspends the binding until the destination is separately
authorized and the user explicitly chooses whether to move it. Tab duplication always receives a new Page Binding.

The Extension uses required `activeTab` and `scripting` permissions for user-invoked temporary access and optional
HTTPS host permissions for exact-site persistence. It never requires `<all_urls>`. Existing fixed-site Probe access
and the complete GSMTC-only path remain available independently.

The Router-facing future interface exposes neutral target identity, capabilities, observations and one-shot command
results. Chrome tab IDs, document IDs, DOM details, site permissions and Native Messaging envelopes remain inside the
Browser Adapter Module.

## Consequences

- Two pages with identical titles remain distinct targets.
- Permission loss is an observable target-preserving failure, not permission to route to a competitor.
- Unsupported, DRM-only, Canvas-rendered, inaccessible-frame and ambiguous pages advertise no false direct
  capability and remain GSMTC-capable when the browser publishes a Session.
- Browser restart Recovery remains unavailable unless the Extension can prove Page Binding continuity.
- Production settings, migration and packaging do not change during the disposable Probe.
