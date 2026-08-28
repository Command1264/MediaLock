# Separate reported and effective playback rate

## Status

Proposed for Phase 18 implementation on 2026-08-28.

## Context

`MediaTargetPresentation.PlaybackRate` currently defaults to `1d`. A caller therefore cannot distinguish a provider
that authoritatively reported 1× from one that omitted or supplied an unusable value. WPF advances the observed
timeline with that value and wall-clock elapsed time. This works for a valid reported rate, but cannot correct a missing
rate, continuously detect a later rate change or express why the presentation chose a value.

Rate inference requires rolling same-target observations, monotonic time, outlier handling, confidence, hysteresis and
explicit discontinuity resets. Putting those rules in WPF or individual providers would duplicate policy and let UI
interpolation contaminate later estimates. Extending the Router would give presentation math authority over routing.

## Decision

Represent the provider value as an optional Reported Playback Rate and project a separate Effective Playback Rate with
source `Reported`, `Estimated` or `Fallback` plus bounded confidence. A finite in-range reported value is authoritative;
a missing or invalid value may use a confident estimate and otherwise falls back to 1×.

One concrete, pure Core `PlaybackRateEstimator` owns per-`MediaTargetId` samples, robust slope estimation, confidence,
hysteresis, tolerances and resets. Application supplies authoritative provider observations with timestamps obtained
from its injected monotonic `TimeProvider`, owns catalog lifecycle and publishes the resolved value. WPF consumes that
projection for timeline interpolation and never feeds its interpolated position back.

Seek, non-Playing state, Recovery, reconnect, target／document replacement, invalid bounds, non-monotonic observations
and discontinuous positions discard affected confidence. Estimator state is runtime-only and cannot influence Media
Target identity, Route Decision, Recovery, command capability or persistence.

## Alternatives considered

- Estimate in WPF. This gives the shortest initial call path, but leaks sample windows and confidence into the caller,
  encourages interpolated positions to become observations and duplicates policy across future presentation surfaces.
- Estimate inside each provider Adapter. This keeps raw data close to its source, but gives GSMTC and Browser different
  behavior and makes cross-provider acceptance harder to state or test.
- Return a fully projected position from the estimator. This further simplifies WPF, but combines rate resolution with
  timeline interpolation, Seek preview and display refresh responsibilities. Keeping the estimator rate-only preserves
  the existing timeline boundary and a smaller change surface.
- Add an estimator `I*` Interface and Adapter immediately. Only one pure in-process implementation is planned, so this
  would add indirection without a second dependency or test boundary.

## Consequences

The change requires an explicit migration from the ambiguous `double PlaybackRate = 1d` contract and updates every
provider projection and affected test. In exchange, default 1× no longer hides missing data, the estimation policy has
high locality, and GSMTC／Browser callers share one provider-neutral behavior.

Confidence introduces deliberate convergence delay during a rate change. Until enough sustained evidence exists, the
UI retains the previous confident value or the neutral fallback according to the estimator contract rather than
oscillating between noisy slopes. There is no speculative Adapter or persistence schema for this single in-process
implementation.
