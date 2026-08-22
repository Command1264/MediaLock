# Phase 6 packaged validation

## Preliminary host smoke

### Environment

- Date: 2026-08-22 (Asia/Taipei)
- Host: Windows 25H2 build 26200.9168, x64
- Artifact: local `0.2.0-rc.1` test artifact from source commit
  `e003e5cc3da90f30b03b85e214ccec0144bf270a`
- Provenance: `sourceDirty: true`; this was an implementation-test artifact, not a formal candidate
- SHA-256: `5d05003dbe4baad3014e9d823670d78caf3352f2693f3396f01a8799d74b8dff`
- Signature: unsigned (`NotSigned`), as declared by the manifest
- Archive size: 78,603,973 bytes
- Executable size: 200,144,769 bytes

### Results

1. Independently recomputed SHA-256 matched the manifest: pass.
2. ZIP expanded to exactly one `MediaLock.exe`: pass.
3. EXE metadata reported ProductVersion `0.2.0-rc.1` and FileVersion `0.2.0.0`: pass.
4. Cold launch showed one main window and discovered the existing Brave YouTube Music and ordinary YouTube GSMTC
   Sessions: pass.
5. Launching the packaged EXE a second time retained one process/window and activated the existing instance: pass.
6. Closing the main window kept the exact packaged process running with no targetable main window; launching the EXE
   again restored that window: pass (Close-to-Tray and single-instance activation).
7. Packaged Settings opened and showed the read-only `Priority Rules` startup mode plus the two persisted application
   rules: pass. No settings were changed or saved during this smoke.
8. The exact packaged test process was stopped after automation and no matching process remained: pass.

### Limitations

- Notification-area `Exit` could not be targeted reliably by the available window automation, so packaged Exit
  remains a manual step. Existing production-build Exit evidence is not substituted for this artifact gate.
- Login-startup registration was not toggled to avoid changing the host's persisted configuration.
- Windows Sandbox or another clean supported x64 Windows environment has not yet run the candidate.
- A formal artifact must be rebuilt after review/commit with `sourceDirty: false`; this test hash is not a release
  hash.

These limitations apply only to the preliminary dirty artifact above. The formal candidate and clean-environment
results below supersede them as release-gate evidence.

## Formal candidate and clean-environment smoke

### Environment and provenance

- Date: 2026-08-23 (Asia/Taipei)
- Host: Windows 11 Pro 25H2 build 26200.9168, x64
- Sandbox application: Windows Sandbox `0.8.107.0`
- Sandbox guest: Windows 11 Enterprise 24H2 build 26100.9168, x64. `winver` and
  `Win32_OperatingSystem.Caption` reported Windows 11 Enterprise; the legacy Registry `ProductName` and
  `Get-ComputerInfo.WindowsProductName` fields reported `Windows 10 Enterprise`, while `DisplayVersion: 24H2`,
  `EditionId: Enterprise` and the build consistently identified the guest
- Artifact: formal `0.2.0-rc.1` candidate from source commit
  `a2e85007ec570344ab91518f0b1de918605be8a0`
- Provenance: `sourceDirty: false`
- .NET SDK: `10.0.400`
- Runtime: `win-x64`, self-contained, single-file, untrimmed
- Signature: unsigned (`signed: false`); no SmartScreen warning appeared in this Sandbox run, but that observation
  does not replace the unsigned-package warning
- SHA-256: `e1791f621d050165977b062343bf1febc8901eacee61a1b5db080aefd3a466c5`
- Archive size: 78,603,934 bytes
- Executable size: 200,144,769 bytes

### Procedure

1. Start a new Windows Sandbox and copy only the ZIP, manifest and checksum into `Desktop\MediaLock-RC`.
2. In Sandbox Windows PowerShell, parse the manifest, run `Get-FileHash` over the ZIP, compare the two hashes, run
   `Expand-Archive`, and enumerate `extracted` with `Get-ChildItem`.
3. Confirm no `MediaLock` process exists, then launch `extracted\MediaLock.exe` with `Start-Process`. Observe the
   initial mode and notification-area icon without installing a .NET runtime.
4. Launch the same EXE again, wait two seconds, and count `Get-Process MediaLock` results. Confirm the first window
   is activated instead of creating another instance.
5. Open Settings, invert `Close the main window to the notification area`, save, reopen and verify the changed value;
   restore the original value, save and verify it again.
6. Recursively enumerate `%LocalAppData%\MediaLock`, parse `settings.json` and `state.json` with `ConvertFrom-Json`,
   and confirm the bounded `logs` output exists.
7. Enable `Start Media Lock when I sign in to Windows`, save, and compare the current-user `MediaLock` Run value to
   the quoted candidate path plus `--startup`. Reopen Settings, disable the option, save, and confirm the exact value
   is absent. Do not elevate the process.
8. Close the main window, confirm the process and notification icon remain, and restore the window from the icon.
   Right-click the icon, choose `Exit`, wait two seconds, and recount processes and the startup value.
9. Open Edge YouTube, play media, relaunch Media Lock, select the named `MSEdge` Session and choose `Lock session`.
   Send `Pause` and `Play` from Media Lock and observe YouTube after each command. Return to `Windows Auto`, then
   exit through the notification area.
10. Run `Get-ComputerInfo`, query `Win32_OperatingSystem` and
    `HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion`, run `cmd /c ver`, and inspect `winver` to record the
    Sandbox guest version.

### Expected and actual results

| Check | Expected | Actual |
| --- | --- | --- |
| Integrity and layout | Manifest and independently computed SHA-256 match; ZIP contains exactly one `MediaLock.exe` | Both hashes were `e1791f621d050165977b062343bf1febc8901eacee61a1b5db080aefd3a466c5`; `HashMatches: True`; one 200,144,769-byte EXE: pass |
| Cold start | Starts without a separately installed .NET runtime or application error; one window and icon | Started without runtime prompt, security warning, error or crash; initial mode was `Windows Auto`, `Priority Rules` was available, and one icon appeared: pass |
| Single instance | Second launch activates the first instance and leaves one process | Existing window activated; `ProcessCount: 1`, process ID `6980`: pass |
| Settings persistence | Save closes Settings; changed and restored values survive reopening | Both saves closed Settings, and both the changed and restored `Close to tray` values reloaded correctly: pass |
| User files | Settings/state JSON parse and bounded logs exist under the Sandbox user's local application data | `settings.json` and `state.json` parsed successfully; logs existed; no application error or crash was observed: pass |
| Login startup | Exact current-user Run value is added without elevation, persists in Settings, and is reversibly removed | Enabled value matched the quoted candidate path plus `--startup`; reopened Settings stayed checked; removal produced `StartupValueExists: False`: pass |
| Close-to-Tray | Closing hides the window but retains the process/icon; icon restores it | Process and icon remained, and the icon restored the window: pass |
| Explicit Exit | Notification-area Exit removes the process and leaves startup disabled | `ProcessCount: 0`, `StartupValueExists: False`, `StartupValue: [removed]`; no crash: pass |
| GSMTC interoperability | A named source is enumerated and one routed command changes it | `MSEdge` was enumerated, playback status updated, Session Lock succeeded, and both `Pause` and `Play` changed YouTube: pass |
| Guest identity | Record a supported x64 Windows environment using independent version surfaces | CIM and `winver` identified Windows 11 Enterprise 24H2; `FullBuild: 26100.9168`; x64: pass |

### Evidence summary

- Validation completed by approximately 01:20 on 2026-08-23 (Asia/Taipei).
- PowerShell output recorded the exact manifest identity, digest comparison, extracted file count/size, process count
  and startup Registry state summarized above.
- `winver` visibly reported Windows 11 Enterprise, version 24H2, OS build 26100.9168. CIM independently reported
  `Microsoft Windows 11 Enterprise`, version `10.0.26100`, build `26100`.
- Application JSON and log contents were not copied into the repository; only their parse/existence result is
  retained to avoid preserving media or Sandbox-local metadata.

### Release-gate conclusion

The formal candidate passed the documented clean supported Windows gate. It may be described as portable in layout:
the transferred ZIP ran without a separately installed .NET runtime and wrote only current-user state during the
test. The candidate remains unsigned and prerelease-only. Creating a tag, GitHub Release or public artifact still
requires separate approval.
