# Phase 12B footprint optimization plan

## Outcome

Reduce Media Lock's distribution or installed footprint only when the selected artifact keeps its documented runtime
contract and passes a measured startup and functional gate. File size alone is not acceptance evidence.

Implementation was approved on 2026-08-25. Push, PR, merge, tag, Release edits and artifact publication remain
separately authorized remote operations.

## Fixed constraints

- The default portable package remains `win-x64`, self-contained and single-file; a clean Windows installation must not
  require a separately installed .NET runtime.
- `IncludeNativeLibrariesForSelfExtract` remains enabled for the single-file payload.
- WPF／WinForms trimming and Native AOT are unsupported and out of scope.
- ReadyToRun remains disabled because Phase 12B optimizes footprint, not startup at the cost of larger images.
- Portable ZIP and installer continue to consume the same executable unless a separately approved product decision
  changes that contract.
- English and Traditional Chinese must remain complete. Use Windows language must retain the documented neutral
  fallback on unsupported cultures.
- The i7-8700 host is the primary lower-end startup reference. Windows Sandbox remains the clean-machine compatibility
  gate, not the performance reference.

## Measurement contract

`eng/Measure-PublishFootprint.ps1` creates isolated baseline and candidate outputs, then records:

- single-file EXE, ZIP and Inno Setup sizes;
- bundle extraction-cache bytes under a candidate-specific `DOTNET_BUNDLE_EXTRACT_BASE_DIR`;
- fresh-extraction-cache and warm-extraction-cache startup samples;
- median, p95, minimum and maximum startup time;
- CPU, logical processor count, memory, Windows build, .NET SDK and source commit; and
- raw samples plus comparisons in JSON and Markdown.

Every primary run uses at least seven samples per startup kind and alternates variant order. A final candidate uses at
least 15 samples. The tool refuses to run startup measurements while another MediaLock process exists and terminates
only the exact process it created.

The automated benchmark does not flush the Windows file cache. A reboot-based first-launch smoke on the i7-8700 is a
separate manual gate and must be described as such.

## Decision gates

### Startup

A candidate is eligible only when all conditions hold on the i7-8700 reference host:

- fresh-extraction-cache median regression is at most 10% and 150 ms;
- warm-extraction-cache median regression is at most 10% and 100 ms;
- p95 has no unexplained outlier or repeated timeout;
- the main window remains responsive immediately after measurement readiness; and
- a reboot-based manual comparison reports no material interaction delay.

The exact clean-commit 15-sample single-file-compression run passed these startup thresholds: fresh median `+47.45 ms`
（`+2.86%`）and warm median `+32.84 ms`（`+2.04%`）.

### Footprint

Every artifact surface is reported independently:

- public download: ZIP and Setup;
- installed／extracted payload: `MediaLock.exe`;
- runtime extraction: `%TEMP%\.net` equivalent cache; and
- optional framework-dependent comparison: app payload plus the separately required Desktop Runtime.

No result may call a candidate “smaller” without naming which surface changed. A Setup or ZIP regression requires an
explicit product decision even when the installed EXE shrinks.

### Functional compatibility

The selected candidate must pass:

1. cold launch and second-instance restore;
2. Light／Dark and English／Traditional Chinese／Windows language;
3. Settings persistence, Tray restore and explicit Exit;
4. GSMTC enumeration, metadata, artwork and timeline;
5. physical Play/Pause, Next／Previous and competing-source isolation;
6. Lock session, Lock app, Priority Rules and Windows Auto;
7. Recovery, lock/unlock, sleep/wake and login startup;
8. installer upgrade, uninstall and retained user data; and
9. valid settings/state/log JSON with no Error／Critical entry.

## Implementation slices

### 1. Reproducible benchmark

Add the measurement tool and a fast contract test. Keep generated executables, caches and reports under ignored
`artifacts/`; do not commit machine-specific paths or raw user data.

### 2. Primary single-file compression experiment

Compare the existing publish profile with `EnableCompressionInSingleFile=true` while holding every other property
constant. Measure the same payload through ZIP and the pinned Inno Setup compiler. Do not change the official profile
until startup, footprint and product tradeoffs are accepted.

### 3. Supported-locale experiment

Measure `SatelliteResourceLanguages=zh-Hant;zh-TW` both with and without single-file compression. Treat filtering as a
behavioral change because it removes framework satellite resources. Adopt it only after the localization matrix passes
on the host and clean Windows Sandbox.

### 4. Select one release candidate

The first measurement found:

| Candidate | EXE | ZIP | Setup | Startup |
| --- | ---: | ---: | ---: | --- |
| single-file compression | -58.91% | -2.78% | **+37.24%** | fresh +2.86%; warm +2.04% |
| supported locales | -9.11% | -6.98% | -3.46% | not expected to decompress; still requires final measurement |
| locales + compression | -61.95% | -10.49% | **+26.64%** | requires final measurement |

The accepted release candidate is single-file compression with all language resources retained. The user explicitly
accepted the larger installer download in exchange for the much smaller installed／portable EXE after reviewing the
i7-8700 startup results. Supported-locale filtering remains test-only because it removes framework localization assets.

### 5. Integrate and document

After selecting a candidate, update the publish profile, release manifest metadata, packaging assertions, roadmap,
testing guide and release runbook together. Record the exact source commit and final benchmark report without committing
machine-specific executable artifacts.

## Deferred decisions

- A framework-dependent download requiring .NET 10 Desktop Runtime.
- Splitting portable and installer payload settings.
- Multi-file self-contained distribution.
- Replacing the WinForms Tray dependency with a native or WPF-only adapter.
- Code signing, automatic updates and architecture-specific packages.
