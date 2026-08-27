# Use provider-neutral Media Targets in production routing

## Status

Accepted for the Phase 16C provider-neutral Core／Application seam on 2026-08-27.

## Context

The production Router currently resolves and controls GSMTC Media Sessions. Phase 16 proved that a Browser Adapter can
instead control one authorized page through an exact Page Binding and short-lived Browser Media Endpoint. Treating
that page as a synthetic GSMTC Session would make browser identity depend on fields that GSMTC does not own, while a
second browser-only Router path would duplicate Routing Mode, Recovery, capability and exactly-once policies.

## Decision

Core and Application route through one provider-neutral Media Target seam. The GSMTC
Adapter and Browser Adapter are peer implementations. The seam exposes provider-qualified immutable identity,
capabilities and observation, target availability changes and a one-shot Media Command result; provider transport
handles and permission details remain inside their Adapter Modules.

Browser identity uses the Page Binding／Browser Application Scope model accepted for the disposable Probe by
[ADR 0005](0005-use-page-bindings-for-browser-media-targets.md). It is never reconstructed from a GSMTC
`SourceAppUserModelId`, browser executable, origin, URL, title or tab order. Existing GSMTC persistence keeps its exact
meaning, and new browser selectors carry an explicit provider and selector kind.

Provider absence adds no direct targets and leaves Media Lock GSMTC-only. Loss of an already-bound Browser Media Target
preserves that target and enters Recovery／Unavailable; it does not authorize routing to a competing GSMTC Session or
page. A mutating command crosses the selected Adapter once and is never retried after an unknown outcome.

Visible-target reconciliation suppresses a GSMTC target only when a present direct target carries authoritative,
exact correlation to that exact GSMTC identity. Browser executable, title, URL, origin similarity, tab order and track
metadata never establish that relation. Uncorrelated Brave or other browser GSMTC targets remain visible and
controllable as fallback.

## Considered options

- **Synthetic GSMTC Session:** rejected because it conflates Page Binding and Session Fingerprint identity and leaks
  browser lifecycle semantics into existing GSMTC recovery.
- **Separate browser Router:** rejected because policy and safety behavior would diverge across providers.
- **Provider-neutral Media Target seam:** selected because two proven Adapters justify one real seam while keeping
  provider-specific complexity local.

## Consequences

The existing GSMTC adapter is exposed through `IMediaTargetCatalog` and `IMediaTargetController`; Core／Application
state, expected-target capture, route decisions and Playback State Lock use provider-qualified identity while the
current UI retains an explicit GSMTC Session projection. This accepted seam ships no Browser Adapter, Extension, UI,
settings migration or package integration. Browser Session Lock with Play, Pause and bounded Seek is the next
independent slice; other Routing Modes, persistence, authorization UI and packaging remain later gates. The complete
GSMTC-only composition remains supported and independently tested when no Extension is installed.
