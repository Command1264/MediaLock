# Phase 6 Release candidate runbook

## Scope and release status

The candidate is `0.2.0-rc.1`, targeting `win-x64` as a self-contained single-file WPF application. It packages the
implemented Session Lock, App Lock, Priority Rules, Recovery, tray, settings and input-routing behavior. Artwork,
seek, volume, customizable shortcuts and browser integration remain outside this candidate.

The candidate is unsigned. Its manifest records `signed: false`; Windows may therefore show reputation or
SmartScreen warnings. Only continue with an artifact whose SHA-256 matches a trusted build. Do not describe the ZIP
as portable until the clean-environment gate below passes.

Single-file publication embeds native libraries for extraction. On Windows, .NET can extract bundled files beneath
`%TEMP%\.net` while the program runs. Trimming and ReadyToRun are disabled for this candidate.

## Build from a reviewed commit

Prerequisites:

- Windows x64.
- PowerShell 7.
- The .NET SDK selected by `global.json` (`10.0.400`, with latest-patch roll-forward).
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
& .\eng\Publish-ReleaseCandidate.ps1 -Version 0.2.0-rc.1
```

The command refuses to overwrite existing outputs and refuses dirty source by default. It fingerprints tracked and
untracked source before and after publication, then withholds all final outputs if source content or `HEAD` changed
during the build. `-AllowDirty` exists only for explicitly disclosed test artifacts; its manifest sets
`sourceDirty: true`.

Expected files:

- `artifacts\MediaLock-0.2.0-rc.1-win-x64.zip`
- `artifacts\MediaLock-0.2.0-rc.1-win-x64.manifest.json`
- `artifacts\MediaLock-0.2.0-rc.1-win-x64.sha256`

The ZIP must contain exactly one file named `MediaLock.exe`. The manifest is the source of truth for version, source
commit, SDK, RID, dirty/signing state and archive size/hash.

## Verify a transferred artifact

Place the three files in one directory and run:

```powershell
$archive = '.\MediaLock-0.2.0-rc.1-win-x64.zip'
$manifest = Get-Content '.\MediaLock-0.2.0-rc.1-win-x64.manifest.json' -Raw | ConvertFrom-Json
$actualHash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
$actualHash
$manifest.archive.sha256
```

The two values must match before extraction. A formal candidate also requires `sourceDirty: false`, `signed: false`,
`runtimeIdentifier: win-x64`, `selfContained: true` and `singleFile: true`.

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

## Clean supported Windows gate

Use Windows Sandbox or a disposable x64 Windows VM that has not built Media Lock. Transfer only the three artifact
files, verify the hash, then repeat the host smoke test items that do not require preinstalled media applications:

- cold start without a separately installed .NET runtime;
- one window/process/icon after second launch;
- Settings save and user-file creation;
- reversible current-user startup registration;
- tray resources and explicit Exit without an orphan process.

Install or open a named GSMTC-capable source only when the environment policy permits it, then verify Session
enumeration and one routed command. A host-only pass is not a clean-environment pass.

## Rollback and cleanup

Media Lock is portable in layout and has no installer transaction. To roll back, exit the candidate and start the
previous trusted executable. Preserve `%LocalAppData%\MediaLock\` before investigating a failure; do not delete user
settings as a routine rollback step. If login startup was enabled, disable it from the running candidate before
rollback or remove only the exact current-user `MediaLock` startup entry after confirming its target.

Publishing a tag, GitHub Release, signed package or public artifact is a separate remote operation requiring explicit
approval after all release gates pass.

Host-side packaged smoke evidence is recorded in [Phase 6 host smoke](phase-6/host-smoke.md). Clean-environment
evidence remains a separate release gate.
