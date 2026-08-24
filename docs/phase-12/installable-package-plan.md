# Phase 12A installable package plan

## Outcome

Add a per-user Inno Setup installer beside the existing portable ZIP. A successful Phase 12A lets an ordinary Windows
11 user install Media Lock without elevation, find it through Windows Search, upgrade it in place, remove it through
Installed apps and retain a valid optional login-startup path.

Implementation was approved on 2026-08-25. Public artifact publication, signing, tag and Release changes remain
separately authorized operations. Inno Setup is a build-only tool and is not a Media Lock runtime dependency.

## Fixed product contract

- Installer name: `MediaLock-Setup-<version>-win-x64.exe`.
- Install scope: current user with no UAC requirement.
- Stable location: `%LocalAppData%\Programs\MediaLock\MediaLock.exe`.
- Discovery: one current-user Start Menu shortcut named `Media Lock`; no desktop shortcut by default.
- Identity: one permanent Inno `AppId` across compatible releases. The implementation commit records the allocated ID.
- Payload: the same reviewed, self-contained, single-file `MediaLock.exe` used to create the matching portable ZIP.
- User data: `%LocalAppData%\MediaLock\` remains outside the install directory and survives uninstall by default.
- Startup: installer does not enable login startup. Settings continues to write
  `"%LocalAppData%\Programs\MediaLock\MediaLock.exe" --startup` when the user enables it.
- Trust: executable and installer signing state are recorded independently. An unsigned installer remains clearly
  labeled and may be warned about or blocked by Windows security features.
- Distribution: portable ZIP remains supported until the installed upgrade and rollback matrix passes in a clean
  Windows environment.
- Installer language: the first package uses Inno Setup's official built-in English messages. Media Lock itself still
  applies English or Traditional Chinese normally. A reviewed in-repository Traditional Chinese installer language
  file may be added later; the build must not download an untrusted translation.

## Implementation slices

### 1. Create one reproducible payload seam

Refactor the existing publication flow only enough to produce one versioned staging payload from a clean source
snapshot. Both ZIP and installer consume that exact `MediaLock.exe`; neither rebuilds it independently. Preserve the
existing dirty-source guard, before/after source fingerprint, semantic version validation and single-file inspection.

The release manifest must identify both artifacts, their sizes and SHA-256 values, the payload hash, source commit,
SDK, RID, self-contained/single-file/trimming flags and each artifact's signing state. Publication refuses existing
outputs and withholds final outputs if any required artifact fails.

### 2. Add the reviewed installer source

Check in a minimal `.iss` source and a wrapper that locates a pinned supported Inno compiler, supplies version and
payload paths, and fails with an actionable message when the compiler is absent or unexpected. Do not download or
execute tools implicitly from the release script.

The installer uses `PrivilegesRequired=lowest`, the fixed install path and permanent `AppId`; creates only the intended
Start Menu and uninstall registrations; records publisher/support/update metadata; detects the running application and
asks for a normal Exit before replacement; and offers post-install launch without silently enabling startup.

### 3. Protect login-startup ownership

Keep `RegistryLoginStartupManager` as the runtime owner of the opt-in HKCU Run value. Add a non-UI uninstall-cleanup
command to the application that reuses its tested exact-command helper; the installer invokes that command before it
removes the executable, so quoting and path comparison have one definition instead of duplicated Pascal logic.

During uninstall, delete `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\MediaLock` only when its complete string
equals the command for the installed `{app}\MediaLock.exe`. Preserve missing, malformed or portable-owned values.
After an in-place upgrade, Settings and the registry value must agree without the user toggling the setting again.

### 4. Define upgrade, downgrade and uninstall behavior

Use same-`AppId`, same-directory replacement for compatible upgrades. Prevent duplicate Installed apps entries and
shortcuts. Explicitly test the previous supported stable installer to the new installer, then test the documented
rollback direction. If settings-schema compatibility cannot support downgrade, block it with an actionable message
instead of corrupting or deleting user data.

Normal uninstall removes installed program files, shortcuts, uninstall metadata and only the matching startup value.
It leaves settings, runtime state and logs. A future optional user-data removal choice requires separate approval and
must be unchecked with the exact target shown.

### 5. Update user and release documentation

Update README, installation guidance, release runbook and support troubleshooting together. Clearly distinguish
installer and portable procedures, hashes, update/rollback paths, data retention and unsigned warnings. Do not call
the installer auto-updating or transactionally rollback-safe.

## Automated gates

Follow RED → GREEN at the packaging and startup seams:

1. Require the fixed `AppId`, per-user privilege mode, stable directory, shortcut and metadata in installer source.
2. Build ZIP and Setup from one staged payload and assert the recorded payload/source identity matches.
3. Independently recompute ZIP, Setup and payload SHA-256 values and verify manifest/checksum outputs.
4. Reject dirty or changing sources, invalid versions, missing/wrong compiler versions and partial final outputs.
5. Prove exact startup-command matching, including spaces, quotes, case behavior, missing values and a portable path
   that must remain untouched.
6. Run restore, format verification, all Release tests, Release build, portable packaging inspection and installer
   packaging inspection.

No GitHub Actions capacity is assumed; the complete local gate remains mandatory and its absence from a PR is stated.

## Windows Sandbox gate

Record the exact source commit, Setup/ZIP hashes, Windows edition/build and actual unsigned warning. From a clean
Sandbox, verify:

1. cold install without UAC or a separately installed .NET runtime;
2. Start Menu and Windows Search launch, Installed apps metadata and one process/tray icon;
3. Settings, persistence, Edge GSMTC, global media-key isolation and explicit Exit;
4. startup disabled by default, then exact registration and actual login-start behavior after enabling it;
5. previous-version in-place upgrade with one shortcut/uninstall entry, retained data and valid startup path;
6. controlled cancellation/failure with only the rollback behavior actually observed;
7. supported downgrade or an intentional, actionable block;
8. uninstall with startup disabled and enabled, preserving user data and any nonmatching portable startup value;
9. portable ZIP operation and single-instance behavior while the installed copy exists; and
10. readable settings/state/log JSON, no Error/Critical log entries, and a final filesystem/registry/process snapshot.

Phase 12A is complete only when automated gates, review and this exact clean-Windows matrix pass. Publishing the
installer, changing GitHub Release assets, signing, push, PR, merge and remote branch cleanup each require the
applicable explicit authorization.

## Deferred decisions

- Code signing, Azure Artifact Signing and Microsoft Store distribution.
- Automatic updates.
- MSI/WiX enterprise deployment and repair.
- MSIX package identity and packaged StartupTask integration.
- Framework-dependent distribution, single-file compression and all trimming/footprint changes in Phase 12B.
