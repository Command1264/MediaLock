# Phase 13B `0.3.0-rc.1` packaged validation

Do not copy values or results from an earlier version. Fill this record only from the exact formal artifacts produced
after the candidate source commit is reviewed and clean.

## Candidate identity

Status: formal local artifacts built and independently inspected on 2026-08-25.

- Version: `0.3.0-rc.1`.
- Source commit: `25690bf138342cdf79ee7b19a4d2e4080e15e38a`.
- Source dirty: `false`.
- Runtime identifier: `win-x64`.
- Self-contained: must be `true`.
- Single-file: must be `true`.
- Single-file compressed: must be `true`.
- ZIP: `MediaLock-0.3.0-rc.1-win-x64.zip`; 76,499,947 bytes;
  SHA-256 `4c0d8694fcdd76a7e90452a2ac0d13941281c5b5e44fab8ab337497adf15d042`.
- Setup: `MediaLock-Setup-0.3.0-rc.1-win-x64.exe`; 76,912,972 bytes;
  SHA-256 `7b8db4499340729df558a6fe99da865069aef9a093b6b2acbc903aa8d69c66bf`.
- Payload: `MediaLock.exe`; 82,314,711 bytes;
  SHA-256 `3868050e0a23079954047c016de25c55500dd14d75f9aba853150ccd2e3d164a`.
- ProductVersion: `0.3.0-rc.1` for payload and Setup.
- FileVersion: `0.3.0.0` for payload and Setup.
- Executable and Setup signatures: independently verified `NotSigned`.

Both recomputed container digests matched manifest schema 3 and their standalone checksum files. The expanded ZIP
contained exactly one `MediaLock.exe`; its recomputed digest matched the manifest payload digest. Manifest RID,
self-contained, single-file, compression, trimming and signing fields matched the planned contract.

## Automated gate

Status: passed on 2026-08-25 against exact source commit
`25690bf138342cdf79ee7b19a4d2e4080e15e38a`.

- Restore and formatting verification passed.
- All 344 Release tests passed; Release build completed with 0 warnings and 0 errors.
- Publish and footprint contract tests passed, including PowerShell 7 and Windows PowerShell 5.1 prerelease artifact
  selection.
- ZIP／Setup one-payload identity, manifest schema, hashes, unsigned state and clean-source guards passed.
- Markdown relative links and `git diff --check` passed.
- Standards review ended with 0 findings. Spec review found one missing post-downgrade startup assertion; commit
  `25690bf` added a fresh Registry read and ordinal full-command comparison, after which focused re-review ended with
  0 findings.

## Local host smoke

Status: pending.

Record Windows build, i7-8700 host identity, ASUS ROG STRIX FLARE keyboard, named YouTube Music and ordinary YouTube
sources, Routing Mode, exact candidate source commit and both container hashes. Verify:

1. Cold launch, one process/icon, second-instance restore, Settings and Tray Exit.
2. Windows／English／Traditional Chinese, Windows／Light／Dark, About and privacy-safe diagnostics.
3. Four Routing Modes, Play/Pause/Next/Previous/Stop, routed Seek and competing-source isolation.
4. Keep Playing correction, Media Lock command override, repeated-pause escape and Windows lock-screen override.
5. Recovery, lock/unlock and sleep/resume without duplicate routing or a stuck Unavailable state.
6. Startup enable/disable, readable settings/state/log JSON and zero unexpected Error／Critical lines.
7. Ordinary launch responsiveness remains consistent with the accepted Phase 12B profile; no new performance claim is
   made unless the publish profile changes and the quantitative benchmark is repeated.

## Windows Sandbox gate

Status: pending.

Record Windows edition/display version/full build/architecture and the exact candidate hashes. Verify:

1. ZIP and Setup hashes, manifest fields, one-file extraction, ProductVersion/FileVersion and unsigned state.
2. Portable cold start without a separately installed .NET runtime.
3. Per-user Setup without UAC, fixed path, Search／Start Menu, Installed apps and single-instance behavior.
4. Startup disabled by default; enabling and actual relogin use the exact installed path.
5. Settings/state created by the real public portable `0.2.0` remain readable after installing the candidate.
6. Generated predecessor Setup upgrades in place to `0.3.0-rc.1`; the older installer is blocked with exit code 7,
   same-version repair is allowed, and Ready-page cancellation preserves the installed version/data/startup command.
7. Edge appears as `MSEdge`; one routed command and supported Seek work without a competing source changing.
8. Uninstall removes installed files/registration and only an installer-owned startup value while retaining user data
   and a portable-owned startup value.
9. Final process count is zero; settings/state/log JSON remain valid with no unexpected Error／Critical entry.

Host evidence does not replace Sandbox evidence. Record inherited or unavailable hardware rows explicitly instead of
silently treating them as passed.

## Publication

Status: not authorized and not started.

After the exact manifest source commit is integrated into `develop`, a separate approval is required for signed
annotated tag `v0.3.0-rc.1`, GitHub Prerelease creation and public ZIP／Setup upload. Do not upload manifest or standalone
checksum files under the current publication policy, and do not mark the prerelease Latest.
