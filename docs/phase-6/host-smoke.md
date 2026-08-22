# Phase 6 packaged host smoke

## Environment

- Date: 2026-08-22 (Asia/Taipei)
- Host: Windows 25H2 build 26200.9168, x64
- Artifact: local `0.2.0-rc.1` test artifact from source commit
  `e003e5cc3da90f30b03b85e214ccec0144bf270a`
- Provenance: `sourceDirty: true`; this was an implementation-test artifact, not a formal candidate
- SHA-256: `5d05003dbe4baad3014e9d823670d78caf3352f2693f3396f01a8799d74b8dff`
- Signature: unsigned (`NotSigned`), as declared by the manifest
- Archive size: 78,603,973 bytes
- Executable size: 200,144,769 bytes

## Results

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

## Pending gates

- Notification-area `Exit` could not be targeted reliably by the available window automation, so packaged Exit
  remains a manual step. Existing production-build Exit evidence is not substituted for this artifact gate.
- Login-startup registration was not toggled to avoid changing the host's persisted configuration.
- Windows Sandbox or another clean supported x64 Windows environment has not yet run the candidate.
- A formal artifact must be rebuilt after review/commit with `sourceDirty: false`; this test hash is not a release
  hash.
