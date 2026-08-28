# Phase 18 provider-neutral playback-rate estimation plan

Issue: [#65](https://github.com/Command1264/MediaLock/issues/65)

Status: implementation candidate in validation; not yet merged.

## Goal and boundary

Keep the Now Playing slider and time labels aligned when a provider omits a usable playback rate or changes rate while
Playing. Phase 18 introduces one provider-neutral, presentation-only estimation Module. It does not infer Media Target
identity, change Router policy, dispatch commands, modify Recovery, persist a rate or expand Browser authorization.

## Selected design

Application observes each provider snapshot once, attaches a monotonic timestamp and passes authoritative playback
state, timeline and optional Reported Playback Rate to a concrete Core `PlaybackRateEstimator`. The estimator returns a
finite Effective Playback Rate, source and confidence. Application stores that projection with the target snapshot;
WPF reads it without understanding samples, slope fitting or hysteresis.

Because a composite snapshot republishes cached targets when another provider changes, Application compares each
target's authoritative observation fingerprint before sampling. An unchanged cached target retains its prior
resolution and monotonic anchor while confidence is fresh; only a fresh provider observation advances its estimator
window. If no fresh observation arrives for the full five-second window, Application expires an Estimated result to
Fallback through an injected-monotonic-time confidence worker, even when the entire catalog is silent, and rebases
only the presentation anchor to its bounded already-displayed position. That presentation value never becomes an
estimator sample.

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

Use a five-second rolling window spanning at least three seconds and three useful observations. Derive all valid
pairwise position／monotonic-time slopes and publish their median, which tolerates quantized 0.5× timelines and isolated
jitter better than adjacent-only deltas. Continue sampling for the entire Playing interval. Once an estimate is
published, retain it while a candidate remains within 10%; a larger change must appear twice consecutively in the same
direction before replacing it.

The initial accepted rate range is 0.25× through 4×. This is an estimator validation range, not a provider capability
claim. Explicit reported values remain subject to the product's documented provider bound. Duplicate／reversed time,
negative elapsed time, position reversal without Seek, bounds change and large unexplained jumps invalidate the sample
or reset the target. Until confidence is sufficient, publish 1× Fallback.

Per-target samples retain only the five-second window, and the estimator retains at most 256 least-recently-used target
states as a second safety bound. These constants remain private to the Module and may be tuned without changing callers.

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
2. Resolve all targets independently, preserve cached-provider anchors and remove state for targets that leave the
   catalog.
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
