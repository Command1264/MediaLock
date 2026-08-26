# Phase 16B generic web media Adapter plan

Date: 2026-08-26

## Objective

Extend Browser Direct beyond YouTube without granting persistent access to every website. A generic Adapter controls
compatible standards-based web media on a page the user explicitly authorizes. Site Adapters remain optional
capability enrichments; the complete `0.3.0` GSMTC path remains available when the Extension or a direct capability is
absent.

The product promise is **authorized compatible web media**, not every audiovisual website. Directly hosted／cloud MP4,
ordinary `<video>`／`<audio>` and many streaming pages are candidates. DRM-only, Canvas-rendered, inaccessible iframe,
multi-player ambiguous or privately implemented players may remain GSMTC-only.

## Permission model

1. `activeTab` plus `scripting` grants one user-invoked, temporary binding to the current page without an install-time
   all-sites warning.
2. `optional_host_permissions` lets the user explicitly grant one exact site for persistent discovery and Recovery.
3. The Extension never requests required `<all_urls>` access. An all-sites choice, if ever offered, is a separate
   explicit user action and is not required for Media Lock operation.
4. Revocation is observable and immediately removes direct capability without deleting or disabling GSMTC settings.

The Extension explains whether a binding is temporary or persistent before requesting permission. Permission results
are settings changes and must immediately refresh target discovery, rules, status and diagnostics.

## Identity model

```text
Browser Media Target (persisted logical identity)
├─ provider identity
├─ browser installation／profile identity
├─ Browser Application Scope (exact origin or installed Web App identity)
└─ Page Binding (opaque, Extension-issued)

Browser Media Endpoint (live command identity)
├─ active connection generation
├─ tab and document generation
├─ frame
└─ selected media endpoint
```

The page, content script and Native Host payload cannot choose authoritative identity. The Extension service worker
creates Page Bindings from a user gesture and derives live endpoint facts from browser-provided sender／tab／document
data. Title, artist, media URL, page URL, favicon and browser executable are bounded presentation or Recovery evidence;
none can independently recreate a binding.

Reload or same-origin navigation invalidates the Endpoint and may resolve a successor only under the same Page Binding.
Cross-origin navigation suspends the binding until the destination is separately authorized and the user explicitly
confirms whether the binding moves. Tab duplication creates a new binding. Browser restart Recovery must prove binding
continuity; otherwise it reports unavailable and never guesses from URL or tab order.

## Routing-mode contract

| Routing Mode | Browser-direct selector | Ambiguity behavior |
| --- | --- | --- |
| Session Lock | Exact Page Binding | Recover only that binding; otherwise unavailable |
| App Lock | Exact Browser Application Scope | Apply deterministic page candidate policy; no list-order choice |
| Priority Rules | Typed page or application selector, visibly labelled | Keep same-browser pages as independent ordered entries |
| Windows Auto | No persisted selector; exact current page projection | Policy may change page, but every decision names the resolved page |

The UI always shows a human-readable page／Web App label plus browser-profile qualifier and makes the selector scope
available in details／accessibility text. Selecting a direct browser target must never create a raw `Chrome` or `Brave`
rule. Existing GSMTC rules retain their exact `SourceAppUserModelId` behavior and schema migration cannot reinterpret
them as Browser Media Targets.

## Generic Adapter behavior

- Enumerate bounded candidate `HTMLMediaElement` endpoints in every authorized frame.
- Prefer no endpoint implicitly when multiple candidates remain plausible; ask the user to bind one.
- Advertise only observed capabilities. The first generic command set is Play, Pause and Seek.
- Dispatch exactly once to the bound document／frame／endpoint and wait for an explicit result.
- Invalidate an Endpoint on navigation, frame removal, media replacement, permission revocation or Port disconnect.
- Never execute arbitrary JavaScript, selectors, URLs or page-provided privileged commands.
- Bound metadata and timeline inputs before they cross Native Messaging; do not log full page／media URLs or tokens.

Site Adapters may add Next, Previous, Stop, queue or richer metadata only behind the same interface and with separate
evidence. Generic fallback does not use simulated clicks or undocumented site globals.

## Test gates

1. **Permission** — active-tab binding works once; exact-site grant persists; denial／revocation is immediate; no
   Extension profile remains fully GSMTC-capable.
2. **Identity** — two playable pages in one browser and identical titles never merge; duplicate tab gets a new binding;
   stale document／frame／endpoint messages fail closed.
3. **Commands** — Play, Pause and Seek execute once on directly hosted MP4, cloud MP4 and an ordinary streaming page;
   the competing page does not change.
4. **Frames and ambiguity** — same-origin／cross-origin iframe permissions are enforced; multiple plausible elements
   require explicit selection.
5. **Recovery** — reload, same-origin navigation, cross-origin navigation, tab close, Extension reload, browser restart
   and Native Host restart never recover through title, URL similarity or list order.
6. **Routing modes** — Session Lock, App Lock, Priority Rules and Windows Auto preserve their selector scopes and show
   the exact resolved page.
7. **Unsupported media** — DRM／Canvas／private players expose no false capability and remain available through GSMTC
   when the browser publishes a Session.

Phase 16B begins only after the fixed-site Phase 16A Gate A has reliable exactly-once and stale-target evidence. It does
not modify production packaging or persistence until the identity model has an approved ADR and compatibility tests.

## Platform references

- [Chrome `activeTab` permission](https://developer.chrome.com/docs/extensions/develop/concepts/activeTab)
- [Chrome optional permissions](https://developer.chrome.com/docs/extensions/reference/api/permissions)
- [Chrome Scripting API](https://developer.chrome.com/docs/extensions/reference/api/scripting)
- [Chrome content scripts and frame matching](https://developer.chrome.com/docs/extensions/reference/manifest/content-scripts)
