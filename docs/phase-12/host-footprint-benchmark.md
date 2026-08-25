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

## Pending manual release gates

The profile must not be described as ready to ship until these checks pass against the compressed executable:

1. reboot-based first launch and immediate main-window interaction on this i7-8700 host;
2. Light／Dark and English／Traditional Chinese／Windows language;
3. Settings, Tray restore, second instance and Exit;
4. GSMTC metadata, artwork, timeline and physical media keys;
5. Lock session／app, Priority Rules, Windows Auto, Recovery and competing-source isolation;
6. lock／unlock, sleep／wake and login startup; and
7. clean Windows Sandbox install, launch, upgrade／uninstall and retained data.
