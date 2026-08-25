# Phase 13B `0.3.0-rc.1` packaged validation

Do not copy values or results from an earlier version. Fill this record only from the exact formal artifacts produced
after the candidate source commit is reviewed and clean.

## Candidate identity

Status: pending formal artifact build.

- Version: `0.3.0-rc.1`.
- Source commit: pending.
- Source dirty: must be `false`.
- Runtime identifier: `win-x64`.
- Self-contained: must be `true`.
- Single-file: must be `true`.
- Single-file compressed: must be `true`.
- Executable and Setup signatures: expected `NotSigned`; verify independently.
- ZIP／Setup／payload sizes and SHA-256: pending.
- ProductVersion: must be `0.3.0-rc.1`.
- FileVersion: must be `0.3.0.0`.

## Automated gate

Status: pending final exact-commit run.

- Restore, formatting verification, complete Release tests and Release build.
- Publish and footprint contract tests, including PowerShell 5.1 prerelease artifact selection.
- ZIP／Setup one-payload identity, manifest schema, hashes, unsigned state and clean-source guards.
- Markdown relative links and `git diff --check`.
- Code review with no unresolved Critical／High finding.

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
