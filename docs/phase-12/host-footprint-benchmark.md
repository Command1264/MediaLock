# Phase 12B i7-8700 footprint and startup benchmark

## Evidence identity

- Date: 2026-08-25
- Source commit: `214a2b0ab11e7bcb9f8e4c30e682bafb226cf8c7`
- Source dirty: `false`
- .NET SDK: `10.0.400`
- Inno Setup: `6.7.3`
- CPU: Intel Core i7-8700, 6 cores／12 logical processors, 3.20 GHz
- Memory: `51,451,863,040` bytes visible to Windows
- OS: Windows 11 Pro 25H2, build 26200.9168, x64

This record removes machine-specific output paths but preserves the raw samples produced by
`eng/Measure-PublishFootprint.ps1`. Generated executables, extraction caches and installers remain ignored test
artifacts and are not public release assets.

## Method

The tool built an uncompressed-bundle baseline and the accepted single-file-compression candidate from the same clean
commit. Both remained self-contained, single-file, native-self-extract, untrimmed and without ReadyToRun. ZIP used
PowerShell `Compress-Archive -CompressionLevel Optimal`; Setup used the pinned Inno compiler and repository installer
source.

Each variant ran 15 times with a new `DOTNET_BUNDLE_EXTRACT_BASE_DIR`, then once as an excluded warm-up and 15 times
with a persistent extraction cache. Variant order alternated each iteration. Startup ended when the process had an idle
UI thread and a main window. The tool then terminated only the exact PID it launched and waited 500 ms before the next
sample. No MediaLock process remained after the run.

The benchmark did not flush the Windows file cache and is not presented as a reboot-based cold-start measurement.

## Artifact sizes

| Variant | EXE bytes | ZIP bytes | Setup bytes | Extraction cache bytes |
| --- | ---: | ---: | ---: | ---: |
| baseline | 200,339,490 | 78,687,769 | 56,041,226 | 8,215,400 |
| single-file compressed | 82,314,522 | 76,499,762 | 76,911,021 | 8,215,400 |

- EXE: `-58.91%`
- ZIP: `-2.78%`
- Setup: `+37.24%`
- Extraction cache: unchanged

## Startup statistics

| Variant | Fresh median | Fresh p95 | Fresh min／max | Warm median | Warm p95 | Warm min／max |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| baseline | 1,658.47 ms | 1,710.25 ms | 1,623.69／1,710.25 ms | 1,611.87 ms | 1,685.45 ms | 1,579.18／1,685.45 ms |
| single-file compressed | 1,705.92 ms | 1,919.01 ms | 1,659.52／1,919.01 ms | 1,644.71 ms | 1,735.57 ms | 1,616.15／1,735.57 ms |

The candidate fresh median regressed by `47.45 ms`／`2.86%`; warm median regressed by `32.84 ms`／`2.04%`.
Both pass the approved median thresholds. Fresh p95 regressed by `208.76 ms`／`12.21%`; a separate clean 15-sample
run also put the candidate fresh p95 near 1.9 seconds, so the tail cost is reproducible rather than discarded as an
outlier. No startup timed out or exceeded two seconds.

### Raw fresh-extraction-cache samples

Baseline, milliseconds:

```text
1710.25, 1659.15, 1673.28, 1634.06, 1658.47, 1652.21, 1656.92, 1700.55,
1679.01, 1652.76, 1624.98, 1655.13, 1623.69, 1673.16, 1672.36
```

Single-file compressed, milliseconds:

```text
1919.01, 1810.39, 1691.58, 1720.06, 1721.76, 1704.23, 1729.67, 1780.30,
1683.08, 1695.85, 1705.92, 1659.52, 1699.04, 1688.29, 1790.02
```

### Raw warm-extraction-cache samples

Baseline, milliseconds:

```text
1609.26, 1629.20, 1603.20, 1611.87, 1685.45, 1654.91, 1620.76, 1606.45,
1579.18, 1595.93, 1681.51, 1633.75, 1595.50, 1628.51, 1593.16
```

Single-file compressed, milliseconds:

```text
1671.03, 1619.87, 1688.45, 1618.95, 1735.57, 1628.22, 1658.75, 1669.32,
1616.15, 1617.55, 1633.47, 1728.95, 1667.12, 1632.28, 1644.71
```

## Automated gates completed for this source line

- 344 Release tests passed.
- Release build passed with zero warnings and zero errors.
- `dotnet format --verify-no-changes` passed after restore.
- Fast footprint measurement contract passed.
- Clean detached-worktree packaging gate produced and verified ZIP, Setup, manifest schema 3, SHA-256 files and one
  compressed single-file payload.
- Component measurement separated .NET runtime, WPF managed/native, WinForms, WinRT projection and Media Lock payload;
  see [official-source research](../research/dotnet-wpf-publish-footprint.md).

## Host acceptance result

The compressed executable passed the 2026-08-25 host smoke on the same i7-8700 system:

- Main window, Settings, Light／Dark, English／Traditional Chinese／Windows language and the minimum layout remained
  usable. Startup did not feel materially slower to the tester.
- Play／Pause, Next, Previous and Stop routed to the locked YouTube Music source while the competing ordinary YouTube
  source remained unchanged.
- Recovery, Tray restore, second-instance activation and Exit passed. One JSONL log contained 5,756 valid entries and
  no Error／Critical entry.
- Lock／unlock and sleep／wake each retained routing isolation. Sleep／wake entered Recovering and returned to the
  selected source without remaining Unavailable.
- A single reboot observation was completed. A second reboot solely to create a direct baseline/candidate A/B pair was
  explicitly waived by the product owner after the ordinary candidate startup smoke reported no perceptible slowdown;
  the repeatable 15 + 15 sample benchmark above remains the quantitative comparison. This is not recorded as a
  completed reboot A/B comparison.

## Clean Windows Sandbox result

A fresh Windows 11 Sandbox validated a clean artifact built from evidence-recording base commit
`e277736d2abb4586a37af2ef1f961c307d8a4243`. This documentation-only descendant has the same product payload as the
measured implementation commit. The ignored evidence files were not committed as release assets.

- ZIP SHA-256: `9c42790b59370f0a01c0cd8bd7806c56788261387670094b9c69d3d92437dc66`
- Setup SHA-256: `675d14d811caa9648d2e3fda510fe2e5cf0bf5d6ecdb069dda039853e1d9f22d`
- Manifest schema 3, `singleFileCompressed: true`, clean source and unsigned status all matched.
- Silent current-user install created the exact payload, Start Menu shortcut and one Installed apps entry without
  enabling login startup by default.
- A separate fresh launch smoke reached a visible main window; launching the installed executable again retained one
  process at the expected installed path. The initial `state.json` parsed successfully.
- Uninstall removed the application, shortcut, owned startup value and process while retaining user data. A startup
  value owned by a portable copy was preserved.

The Phase 12A upgrade／blocked-downgrade, cancellation and login-startup transaction matrix was inherited rather than
repeated because diff review confirms that Phase 12B changes only published bundle compression, measurement code and
its manifest contract—not the Inno transaction, migration or startup ownership logic. Clean install, launch and
uninstall paths were repeated against the exact compressed payload. The automated suite retained coverage of all
Routing Modes, GSMTC projection and persistence; the exact candidate host smoke repeated physical routing isolation,
Recovery and workstation lifecycle. Host and Sandbox evidence therefore close the risk-scoped Phase 12B acceptance
without claiming a completed reboot A/B pair or a new signed／public artifact.
