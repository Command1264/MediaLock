# Use a current-user Native Messaging bridge for browser targets

## Status

Accepted for the Phase 16C Browser Session Lock candidate on 2026-08-27.

## Context

Chromium Native Messaging launches one short-lived host process per Extension connection. The Media Lock desktop
process owns routing state and must support both Chrome and Brave connections without exposing a network listener or
moving browser protocol details into Core／Application. The Extension, registration and Native Host are still an
unpacked development candidate; no distribution decision has been made.

## Decision

The fixed Extension ID `kggfkkiifnclhhmibdglkbdfbacakemn` connects to
`com.command1264.medialock.browser`. The minimal Native Host validates the exact launch origin and relays bounded
Native Messaging frames to the running Media Lock process through the fixed current-user-only named pipe
`Command1264.MediaLock.Browser.v1`. Media Lock owns the pipe server and the Browser Adapter protocol state.

The bridge accepts only protocol v2 with exact schemas, a 64 KiB frame limit, bounded frame-completion and command
deadlines, fresh nonces, a derived connection identity, strictly monotonic sequences, bounded pending commands and
Play／Pause／bounded Seek. Unknown command outcomes are never retried. Diagnostics expose exception categories only;
page URLs, titles and message payloads are not logged.

The candidate registration script writes only the exact current-user Chrome-compatible Native Messaging value and
uses a content-addressed Host output. Unregistration removes only a value that still points to its exact owned
manifest. Chrome and Brave share this one verified registry seam. The files remain unsigned and same-user replacement
is an explicit development-candidate trust limitation.

## Consequences

Core and Application see only provider-neutral Media Targets. Extension IDs, profile／tab／frame／document identities,
permissions and transport envelopes stay inside the Browser Module. No Extension means no Browser targets and no
prompt; GSMTC remains unchanged. Losing an already locked Browser target preserves its exact identity and never
routes to an uncorrelated Brave／Chrome GSMTC Session.

This ADR does not approve Extension-store distribution, installed-package registration, persistence, App Lock,
Priority Rules, Windows Auto browser routing, tagging or release publication. Those remain independent gates.
