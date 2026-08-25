# Release artifact runbook

## Scope and release status

The release under validation is stable `0.2.0`, targeting `win-x64` as a self-contained single-file WPF application.
It promotes the behavior validated in `0.2.0-rc.3` without adding product features. Volume, customizable shortcuts,
Playback State Lock, Windows Media Surface Mirror and browser integration remain outside this release.

The published `v0.2.0` assets remain frozen and portable-only. On `develop`, Phase 12A extends the same command to
produce an unpublic per-user installer for the next release. Re-running the command with version `0.2.0` is development
evidence only and must not mutate or be attached to the existing GitHub Release.

The release is unsigned. Its manifest records `signed: false`; Windows may therefore show reputation or
SmartScreen warnings. Only continue with an artifact whose SHA-256 matches a trusted build. The earlier formal
`0.2.0-rc.1` and `0.2.0-rc.2` passed their own clean-environment gates. Their evidence does not transfer to
`0.2.0-rc.3`. Stable `0.2.0` requires a new clean source commit, archive SHA-256, host gate and Windows Sandbox gate;
none of the RC evidence transfers to the differently versioned executable.

Single-file publication embeds native libraries for extraction. On Windows, .NET can extract bundled files beneath
`%TEMP%\.net` while the program runs. Trimming and ReadyToRun are disabled for this release.

## Build from a reviewed commit

Prerequisites:

- Windows x64.
- PowerShell 7.
- The .NET SDK selected by `global.json` (`10.0.400`, with latest-patch roll-forward).
- Official Inno Setup `6.7.3`; the publish command rejects an absent or different compiler version.
- A clean Git worktree at the reviewed source commit.

Run the automated local gate:

```powershell
dotnet restore MediaLock.sln
dotnet format MediaLock.sln --verify-no-changes --no-restore
dotnet test MediaLock.sln --configuration Release --no-restore
dotnet build MediaLock.sln --configuration Release --no-restore
& .\tests\packaging\Publish-ReleaseCandidate.Tests.ps1
```

Then create the formal artifact:

```powershell
& .\eng\Publish-ReleaseCandidate.ps1 -Version 0.2.0
```

The command refuses to overwrite existing outputs and refuses dirty source by default. It fingerprints tracked and
untracked source before and after publication, then withholds all final outputs if source content or `HEAD` changed
during the build. `-AllowDirty` exists only for explicitly disclosed test artifacts; its manifest sets
`sourceDirty: true`.

Expected files:

- `artifacts\MediaLock-0.2.0-win-x64.zip`
- `artifacts\MediaLock-0.2.0-win-x64.manifest.json`
- `artifacts\MediaLock-0.2.0-win-x64.sha256`
- `artifacts\MediaLock-Setup-0.2.0-win-x64.exe`
- `artifacts\MediaLock-Setup-0.2.0-win-x64.sha256`

The ZIP must contain exactly one file named `MediaLock.exe`. Manifest schema 2 is the source of truth for version,
source commit, SDK, Inno version, RID, dirty/signing state, payload hash and each container's size/hash. ZIP and Setup
must be produced from that one staged payload; neither may independently rebuild it.

## Verify a transferred artifact

Place all five files in one directory and run:

```powershell
$archive = '.\MediaLock-0.2.0-win-x64.zip'
$installer = '.\MediaLock-Setup-0.2.0-win-x64.exe'
$manifest = Get-Content '.\MediaLock-0.2.0-win-x64.manifest.json' -Raw | ConvertFrom-Json
$archiveHash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
$installerHash = (Get-FileHash $installer -Algorithm SHA256).Hash.ToLowerInvariant()
$archiveHash
$manifest.archive.sha256
$installerHash
$manifest.installer.sha256
```

Each pair must match before extraction or installation. A formal release also requires `sourceDirty: false`, both
payload and installer `signed: false`, `runtimeIdentifier: win-x64`, `selfContained: true` and `singleFile: true`.

## Host smoke test

1. Exit every running Media Lock instance from the notification area.
2. Extract the ZIP to a new directory and confirm it contains only `MediaLock.exe`.
3. Start `MediaLock.exe`; confirm one main window and one notification-area icon appear.
4. Start the same EXE again; confirm it activates the first instance and leaves only one process/icon.
5. Confirm discovered Sessions, Routing Mode and media controls render without an error.
6. Open Settings, save a reversible change, reopen Settings and confirm the saved value.
7. Enable then disable `Start with Windows`; confirm both operations succeed without elevation.
8. Use notification-area `Exit`; confirm no `MediaLock` process remains.
9. Confirm `%LocalAppData%\MediaLock\settings.json`, `state.json` and bounded `logs\*.jsonl` remain readable.

Record Windows build, artifact hash, manifest source commit, actual results and any warning shown.

### Installer transaction smoke test

1. Confirm no installed Media Lock entry or fixed install directory already exists; preserve any unrelated portable
   process and startup value instead of overwriting them.
2. Run Setup as the current user and confirm no UAC, one
   `%LocalAppData%\Programs\MediaLock\MediaLock.exe`, one Start Menu shortcut and one Installed apps entry.
3. Confirm the installed ProductVersion, default-disabled login startup and retained `%LocalAppData%\MediaLock\` data.
4. Enable login startup, uninstall, and confirm only the exact installed-path Run value is removed.
5. Repeat with a Run value pointing to a portable path; uninstall must preserve that value. Remove only the exact test
   value after recording the assertion.
6. Confirm program files, shortcut and uninstall entry are gone while user data remains.

## Clean supported Windows gate

Use Windows Sandbox or a disposable x64 Windows VM that has not built Media Lock. Transfer the five artifact files,
verify both container hashes, then repeat the host smoke test items that do not require preinstalled media applications:

The repeatable PowerShell 5.1 transaction gate is
`tests\packaging\WindowsSandbox-InstallerSmoke.ps1`. Map the five artifacts read-only to
`C:\MediaLockArtifacts`, map a writable results directory to `C:\MediaLockResults`, then run the script inside the
Sandbox. Its JSON result covers hashes, installed payload identity, Start Menu/Installed apps registration, default
startup state, owned/nonowned startup cleanup, user-data retention and final process count. It does not replace the
interactive runtime items below.

For a two-version compatibility gate, place exactly two stable artifact sets under the mapped artifact root and run
`tests\packaging\WindowsSandbox-InstallerUpgradeSmoke.ps1`. It must report a successful in-place upgrade, one
Installed apps entry, retained user data and startup command, followed by downgrade exit code 7 with the newer payload
still installed. Use `tests\packaging\WindowsSandbox-InstallerCancellationSmoke.ps1 -Mode Prepare`, visibly cancel
the newer Setup on its Ready page, then run `-Mode Verify -CancellationExitCode 2`. Record the cancellation stage
precisely; this gate does not establish rollback after file extraction has begun.

- cold start without a separately installed .NET runtime;
- one window/process/icon after second launch;
- Settings save and user-file creation;
- reversible current-user startup registration;
- tray resources and explicit Exit without an orphan process.

Install or open a named GSMTC-capable source only when the environment policy permits it, then verify Session
enumeration and one routed command. A host-only pass is not a clean-environment pass.

## Rollback and cleanup

The ZIP remains portable and has no installer transaction. To roll it back, exit the release and start the previous
trusted executable. An installed release uses same-`AppId`, same-directory replacement for upgrades and same-version
repair. Setup rejects an older complete release version, including RC ordering, because a newer settings schema may not
be backward compatible. Use the currently installed version or a newer installer; do not delete user settings as
routine rollback. If login startup was enabled, disable it from the running release before changing distribution mode,
or remove only the exact current-user `MediaLock` startup entry after confirming its target.

Publishing a tag, GitHub Release, signed package or public artifact is a separate remote operation requiring explicit
approval after all release gates pass. That approval was granted for `0.2.0`: the GPG-signed annotated `v0.2.0` tag
identifies the exact artifact source commit, and the public GitHub Release is neither Draft nor Prerelease and is
designated Latest. The executable remains unsigned.

Historical `0.2.0-rc.1` host-side and clean-environment evidence is recorded in
[Phase 6 packaged validation](phase-6/host-smoke.md), and `0.2.0-rc.2` evidence is preserved in
[Phase 9 packaged validation](phase-9/release-candidate-smoke.md). A different version, source commit or archive digest
always requires new evidence. Historical `0.2.0-rc.3` results remain in
[Phase 10C packaged validation](phase-10/release-candidate-smoke.md); record stable results separately in
[Phase 10D packaged validation](phase-10/stable-release-smoke.md).
