# Use explicit Page Bindings for browser media targets

## Status

Accepted for the Phase 16B disposable Probe on 2026-08-27 and amended for the Phase 16C production candidate on
2026-08-28. Production persistence and packaging remain gated by the complete compatibility matrix and a later
production-integration review.

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

Reload or navigation invalidates every Page Binding owned by that tab as soon as the browser reports `loading`.
The Extension publishes removal for each target. Temporary authority never creates a successor. In Phase 16C, a
retained exact-site permission may authorize the completed replacement top-level HTTPS document automatically, but
that document receives a new opaque Page Binding which is only a catalog candidate: it never repairs, selects or
satisfies the predecessor's lock. A generation token is revalidated after asynchronous binding and immediately before
publication; invalidation discards only that exact stale result and cannot clear a newer same-tab successor. Other
tabs are unaffected, and tab duplication always receives a new Page Binding.

Explicitly authorizing the same tab again replaces its current binding atomically from the desktop's perspective:
the old binding is removed before the new binding is published. This prevents stale opaque identities from remaining
as ghost targets even though the Extension keeps only one current entry for the tab.

The Extension uses required `activeTab` and `scripting` permissions for user-invoked temporary access and optional
HTTPS host permissions for exact-site persistence. It never requires `<all_urls>`. Existing fixed-site Probe access
and the complete GSMTC-only path remain available independently.

The Router-facing future interface exposes neutral target identity, capabilities, observations and one-shot command
results. Chrome tab IDs, document IDs, DOM details, site permissions and Native Messaging envelopes remain inside the
Browser Adapter Module.

## Consequences

- Two pages with identical titles remain distinct targets.
- Permission loss is an observable target-preserving failure, not permission to route to a competitor.
- Reload／navigation intentionally sacrifices Page Binding identity continuity. Exact-site permission may create a
  distinct successor candidate, but lock continuity remains fail closed.
- Unsupported, DRM-only, Canvas-rendered, inaccessible-frame and ambiguous pages advertise no false direct
  capability and remain GSMTC-capable when the browser publishes a Session.
- Browser restart Recovery remains unavailable unless the Extension can prove Page Binding continuity.
- Production settings, migration and packaging do not change during the disposable Probe.
