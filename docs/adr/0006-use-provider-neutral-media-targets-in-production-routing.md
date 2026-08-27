# Use provider-neutral Media Targets in production routing

## Status

Proposed for Phase 16C production-integration review.

## Context

The production Router currently resolves and controls GSMTC Media Sessions. Phase 16 proved that a Browser Adapter can
instead control one authorized page through an exact Page Binding and short-lived Browser Media Endpoint. Treating
that page as a synthetic GSMTC Session would make browser identity depend on fields that GSMTC does not own, while a
second browser-only Router path would duplicate Routing Mode, Recovery, capability and exactly-once policies.

## Decision

If Phase 16C is accepted, Core and Application will route through one provider-neutral Media Target seam. The GSMTC
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

## Considered options

- **Synthetic GSMTC Session:** rejected because it conflates Page Binding and Session Fingerprint identity and leaks
  browser lifecycle semantics into existing GSMTC recovery.
- **Separate browser Router:** rejected because policy and safety behavior would diverge across providers.
- **Provider-neutral Media Target seam:** proposed because two proven Adapters justify one real seam while keeping
  provider-specific complexity local.

## Consequences

The existing GSMTC types must be adapted incrementally behind the new seam before Browser production code is added.
The first accepted slice is Session Lock with Play, Pause and bounded Seek. App Lock, Priority Rules, Windows Auto,
settings migration, authorization UI and packaging remain separate gates. The complete GSMTC-only composition remains
supported and independently tested when no Extension is installed.
