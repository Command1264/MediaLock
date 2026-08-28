# Phase 18 provider-neutral playback-rate estimation plan

Issue: [#65](https://github.com/Command1264/MediaLock/issues/65)

Status: planned; implementation requires separate approval.

## Goal and boundary

Keep the Now Playing slider and time labels aligned when a provider omits a usable playback rate or changes rate while
Playing. Phase 18 introduces one provider-neutral, presentation-only estimation Module. It does not infer Media Target
identity, change Router policy, dispatch commands, modify Recovery, persist a rate or expand Browser authorization.

## Selected design

Application observes each provider snapshot once, attaches a monotonic timestamp and passes authoritative playback
state, timeline and optional Reported Playback Rate to a concrete Core `PlaybackRateEstimator`. The estimator returns a
finite Effective Playback Rate, source and confidence. Application stores that projection with the target snapshot;
WPF reads it without understanding samples, slope fitting or hysteresis.

The public shape is intentionally small:

```csharp
PlaybackRateResolution Observe(PlaybackRateObservation observation);
void Reset(MediaTargetId target, PlaybackRateResetReason reason);
```

Exact names may change during RED tests, but the boundary must preserve these invariants:

- provider-qualified `MediaTargetId` isolates all mutable estimator state;
- a valid Reported Playback Rate wins immediately;
- only authoritative same-target Playing observations with increasing monotonic time contribute samples;
- a result is always finite and in the documented product range;
- discontinuities discard confidence before a new estimate is published;
- no presentation-interpolated position enters the estimator.

## Algorithm contract

Start with a bounded rolling window covering approximately three to five seconds and at least three useful deltas.
Derive candidate slopes from position delta divided by monotonic elapsed time, reject invalid or implausible deltas and
select a robust center such as median slope. Publish Estimated only after sustained agreement within tolerance. Continue
sampling for the entire Playing interval; replace a prior estimate only after consecutive evidence crosses hysteresis.

The initial accepted rate range is 0.25× through 4×. This is an estimator validation range, not a provider capability
claim. Explicit reported values remain subject to the product's documented provider bound. Duplicate／reversed time,
negative elapsed time, position reversal without Seek, bounds change and large unexplained jumps invalidate the sample
or reset the target. Until confidence is sufficient, publish 1× Fallback.

The implementation task must choose and test exact window, tolerance, consecutive-evidence and jump thresholds before
GREEN. These constants remain private to the Module and may be tuned without changing callers.

## Delivery slices

### Slice 1 — explicit rate semantics

1. RED: prove explicit reported 1× differs from missing／invalid rate.
2. Replace the ambiguous defaulted presentation field with optional Reported Playback Rate and a separate resolution.
3. Update GSMTC and Browser provider projections without changing their identity or command paths.

### Slice 2 — Core estimator

1. RED: convergence, jitter, outlier, quantization, rate-change and target-isolation matrix with fake monotonic time.
2. Implement bounded samples, robust slope, confidence and hysteresis behind the small public Interface.
3. RED/GREEN every reset reason and bounded-state eviction.

### Slice 3 — Application projection

1. Timestamp fresh provider observations at the catalog boundary; never timestamp a UI refresh as a new observation.
2. Resolve all targets independently and remove state for targets that leave the catalog.
3. Prove reported override, fallback, Recovery and same-title cross-provider isolation.

### Slice 4 — WPF timeline

1. Advance Playing position from a monotonic anchor and Effective Playback Rate.
2. Keep Pause, bounds clamp, target replacement and Seek preview／confirmation behavior unchanged.
3. Keep rate source／confidence out of the primary media controls. If manual diagnosis needs visibility, expose it in a
   bounded accessible／diagnostic detail without target identity or media metadata; it remains presentation state.

### Slice 5 — regression and human acceptance

Run focused Core／Application／ViewModel tests first, then the complete .NET, Extension, formatting and Release build
gates documented for the repository. Review the full diff on Standards and Issue #65 Spec axes. Only after automated
gates pass, guide the user through the six manual rows in `docs/testing.md`, always reporting `目前第 N／6 項`.

## Failure behavior

Invalid observations do not throw into catalog refresh. They are ignored or reset confidence and produce a bounded
Fallback result. Estimator failure cannot remove a Media Target, alter the Locked Target, enter Recovery or retry a
Media Command. Unexpected implementation exceptions remain ordinary structured Application problems under the Phase
17 contract; Phase 18 does not allocate new public codes unless it adds a distinct user-actionable failure.

## Completion gate

Phase 18 is complete only when all Issue #65 acceptance cases pass deterministically, full relevant regressions pass,
the six manual rows are recorded, documentation matches final thresholds, and no Critical／High review finding remains.
Implementation, push, PR and merge each retain their normal approval boundaries.
