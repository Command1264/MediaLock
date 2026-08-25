# Phase 14B `0.3.0` stable packaged validation

Do not copy identity or runtime results from `0.3.0-rc.1`. Fill this record only from the exact stable artifact
produced after its source commit is reviewed and clean.

## Release identity

Status: pending formal artifact.

- Version: `0.3.0`.
- Source commit: pending.
- Source dirty: must be `false`.
- Runtime identifier: `win-x64`.
- Self-contained: must be `true`.
- Single-file: must be `true`.
- Single-file compressed: must be `true`.
- Signed: expected `false`; verify payload and Setup Authenticode independently.
- Archive: `MediaLock-0.3.0-win-x64.zip`.
- Installer: `MediaLock-Setup-0.3.0-win-x64.exe`.
- Archive SHA-256: pending.
- Installer SHA-256: pending.
- Shared payload SHA-256: pending.

Before host testing, independently recompute both container digests, compare manifest/checksum files, expand exactly
one `MediaLock.exe`, and record ProductVersion `0.3.0`, FileVersion `0.3.0.0` and Authenticode status.

## Automated gate and review

Status: automated gate passed on implementation commit `f5e13838e5fe35c423ef202334206c00c3e89b54`; repeat on the
final artifact-source commit after this evidence update. Follow-up two-axis review is pending.

Record restore, format, complete tests, Release build, all packaging scripts, Markdown relative-link validation,
`git diff --check` and the two-axis Standards／Spec review against the Phase 14 plan. GitHub Actions capacity is not
assumed and no RC test count or result transfers.

Observed RED → GREEN sequence:

- The stable publish contract first failed against the unchanged RC1 project with `MediaLock.App Version must be
  0.3.0`; changing only Version/InformationalVersion made the same contract build and validate stable ZIP／Setup.
- The exact-predecessor contract first failed because the Sandbox scripts did not require a pinned older-installer
  SHA-256. The shared helper now rejects manifest mismatch and pinned-digest mismatch in PowerShell 7 and Windows
  PowerShell 5.1.
- The repair contract first failed because the upgrade script did not execute the newer installer a second time. The
  script now performs stable same-version repair and reports repair exit code plus settings/state invariants.

Gate results on `f5e13838e5fe35c423ef202334206c00c3e89b54`:

- restore and format verification passed;
- 351/351 Release tests passed;
- Release build completed with zero warnings and zero errors;
- publish, artifact-selection and footprint packaging contracts passed; and
- the artifact-selection contract independently passed in PowerShell 7 and Windows PowerShell 5.1.

## Exact-artifact host gate

Status: pending on the i7-8700 Windows 11 x64 reference host.

Use the formal ZIP/Setup and record the full Phase 14 plan matrix: identity, launch/single instance, localization/theme,
Settings/diagnostics, four Routing Modes and controls, Playback State Lock safety/override paths, competing-source
isolation, Recovery, lock/unlock, sleep/resume, actual login startup, Tray and explicit Exit with valid user/log JSON.

## Windows Sandbox gate

Status: pending on a fresh Windows 11 x64 Sandbox.

Record artifact identity, portable launch, per-user install, Search/Start Menu and Installed apps registration,
startup ownership, `MSEdge` GSMTC routing/Seek, real public portable `0.2.0` data compatibility, exact public RC1 Setup
upgrade to stable using the pinned published Setup SHA-256, stable repair with unchanged settings/state, RC1 downgrade
block, Ready-page cancellation, uninstall retention/cleanup and final process/log state. Real sign-out/sign-in is
host-only because Sandbox destroys its environment on sign-out.

## Integration and publication

Status: not started; requires separate approval after every gate above passes.

Record the retained `release/0.3` baseline, PRs into `develop` and `main`, signed annotated tag, public Stable／Latest
Release, public ZIP/Setup assets and independently downloaded GitHub digests only after those operations occur. Never
alter the historical `v0.2.0` or `v0.3.0-rc.1` assets, and retain `release/0.2` throughout Phase 14.
