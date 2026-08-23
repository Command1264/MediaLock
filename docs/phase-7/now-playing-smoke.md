# Phase 7C Now Playing smoke test

Date: 2026-08-23 (automated, production-WPF inspection and focused user acceptance)

## Supported scope

- Artwork and timeline describe the current routed target.
- Timeline is read-only; this phase does not expose seek.
- Artwork is optional and its failure must not affect routing.

## Desktop matrix

| Scenario | Expected | Result |
| --- | --- | --- |
| YouTube Music playing | Correct title, artist, artwork and advancing elapsed time | Pass in production-WPF inspection and user acceptance |
| Pause and resume | Position stops while paused and resumes without jumping outside bounds | Pass in production-WPF inspection and user acceptance |
| Next and Previous | Artwork/timeline change to the new media without retaining stale content | Pass; both previous-track and current-track-to-zero behaviors remain correct |
| Session recreation | Recovering hides stale Now Playing data; recovered target shows current data | Pass in user refresh acceptance |
| Ordinary YouTube competing | Locked YouTube Music remains the displayed and routed target | Pass; ordinary YouTube remained unchanged |
| Missing artwork | Neutral placeholder appears and controls continue to work | Automated malformed/missing-artwork coverage passes; no manual source available |
| Light/Dark and English/Traditional Chinese | Content remains readable at the minimum window size | Traditional Chinese/Dark and English/Light minimum-size user acceptance pass; Windows preferences restored |
| Physical Play/Pause | YouTube Music changes exactly once; ordinary YouTube does not change | Pass in user acceptance |

## Seek capability evidence

Not yet collected. No seek interaction is exposed by Phase 7C.

## Automated and initial production-WPF evidence

- 175 automated tests pass. Coverage includes copied/bounded JPEG and PNG payloads, malformed-image fallback,
  routed-target projection, deterministic playing interpolation, paused stability, target loss and a real WPF
  non-interactive progress control.
- Release build completes with zero warnings and zero errors.
- A production-WPF run discovered both the Brave YouTube Music PWA and an ordinary Brave YouTube Session. The routed
  PWA showed its PNG artwork and a valid read-only timeline. One Play changed the projected state to Playing and the
  elapsed display advanced; one Pause returned it to Paused and the elapsed display remained stable on a later check.
- Selecting the competing ordinary Brave Session did not replace the Priority Rules routed target's artwork or
  timeline. The ordinary Session's observed position remained unchanged during the Play/Pause check.
- Media titles and artist values from this inspection are intentionally not copied into the test record.
