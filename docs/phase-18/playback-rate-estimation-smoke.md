# Phase 18 playback-rate estimation smoke

Status: passed on 2026-08-30 for the exact local candidate below; integration remains open.

## Candidate identity

- Source commit: `de00f080ebf9ee55686c87a59cf6f7bc1e8900bb`.
- Executable: `artifacts/phase18-estimator-candidate/de00f08/MediaLock.exe`.
- Executable SHA-256: `4b90baaf5804527c7e5ac4eee3afb1432c158299b3f62b13b1d754cfc7c340fc`.
- Host: Windows 11 Pro, version `10.0.26200`, x64.
- Browser: Brave `151.1.93.138` with the enabled candidate Extension ID
  `kggfkkiifnclhhmibdglkbdfbacakemn`.
- Media: one authorized long-form Big Buck Bunny HTTPS Browser Media Target competing with a playing YouTube Music
  Session.

The candidate was started directly from the executable path, independently of Codex App. An earlier candidate exposed
a fixed 500-millisecond WPF refresh interval at 3×／10×／16×. That observation did not transfer: the final candidate
adds a rate-aware 50–500 millisecond cadence and received a fresh complete matrix.

## Automated and review gate

- Focused rate-aware ViewModel／WPF cadence tests: 9 passed.
- Complete .NET solution: 543 passed, 0 failed, 0 skipped.
- Browser Extension tests on this Phase branch: 63 passed, 0 failed.
- `dotnet format --verify-no-changes`: passed.
- Release build: passed with 0 warnings and 0 errors.
- Standards review: 0 findings.
- Issue #65 Spec review: 0 findings.

## Manual acceptance — 6／6 passed

1. **1× baseline:** over approximately 20 wall-clock seconds, both the page and Media Lock advanced approximately
   20 seconds. Slider and `mm:ss` remained aligned; YouTube Music was unaffected.
2. **2× measured playback:** over approximately 20 wall-clock seconds, both surfaces advanced approximately 40
   seconds with smooth slider／label cadence and the same locked Browser target.
3. **Continuous and high-rate changes:** 1×／2×／0.5× advanced approximately 12／24／6 media seconds over their
   measured 12-second stages. 3×／10×／16× each remained visibly current after the cadence fix. No stage retained the
   prior rate, moved backward, crossed bounds or stalled longer than two seconds.
4. **External state and Seek:** page-originated Pause stopped the Media Lock timeline; Play resumed it at 2×; one
   page-originated Seek produced one matching position update. Router identity and YouTube Music isolation held.
5. **Reload／replacement:** reload removed the old Browser target and produced exactly one distinct successor. Router
   preserved the unavailable predecessor without selecting Brave GSMTC or YouTube Music; explicit locking restored
   `Locked`, and one Pause／Play controlled only the successor.
6. **Localization／theme:** Traditional Chinese＋Dark, Traditional Chinese＋Light, English＋Light and English＋Dark all
   kept slider, labels and 10× cadence aligned without resource keys, internal rate-source text, clipping or layout
   movement.

No Extension error, warning, unexpected command, duplicate／ghost target or crash was observed in any row. Blank
problem fields were explicitly treated as no problem during the guided run.
