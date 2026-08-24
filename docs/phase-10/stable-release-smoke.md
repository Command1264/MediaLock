# Phase 10D `0.2.0` stable packaged validation

Do not copy identity or runtime results from a release candidate. Fill this record only from the exact stable artifact
produced after its source commit is reviewed and clean.

## Release identity

- Version: `0.2.0`.
- Source commit: `7ce40ab31433998665b30ac18a7f50ebb3dafec7`.
- Source dirty: must be `false`.
- Runtime identifier: `win-x64`.
- Self-contained: must be `true`.
- Single-file: must be `true`.
- Signed: expected `false`; verify Authenticode independently.
- Archive: `MediaLock-0.2.0-win-x64.zip`.
- Executable: `MediaLock.exe`.
- Archive SHA-256: `f368421481fa0a99516618873dfd4e0422c241deae2033b105869471eab27bb0`.

Before host testing, independently recompute the digest, compare the manifest and checksum, expand exactly one
`MediaLock.exe`, and record ProductVersion `0.2.0`, FileVersion `0.2.0.0` and Authenticode status.

## Local host smoke

Status: passed on Windows 11 Pro 25H2, build `26200.9168`, x64.

Use an ASUS ROG STRIX FLARE keyboard, Brave YouTube Music and ordinary Brave YouTube. Verify:

1. Cold start, one process/icon and second-instance activation.
2. About and copied diagnostics show `0.2.0`, Stable and Unsigned with privacy-safe CRLF output.
3. A reversible Settings change persists; login startup enables and disables without elevation.
4. Named Sessions, artwork, timeline, routed controls and Playing/Paused Seek remain correct.
5. One physical Play/Pause affects only the resolved target while the competing source remains unchanged.
6. Recovery, lock/unlock and sleep/resume return to the intended target without duplicate routing.
7. Close-to-tray, restore and explicit Exit leave no orphan process or startup entry.
8. `settings.json`, `state.json` and bounded `logs\*.jsonl` remain parseable with no unexpected Error/Critical entry.

Observed results for source commit `7ce40ab31433998665b30ac18a7f50ebb3dafec7` and the digest above:

- Cold start, tray icon and second-instance activation passed with one process.
- About and privacy-safe diagnostics reported `0.2.0`, Stable and Unsigned. Clipboard output used CRLF only and
  omitted media metadata, user paths, executable paths and the source commit.
- Settings saved and closed normally. Login startup created the exact quoted executable path followed by
  `--startup`, persisted as enabled, and was removed again after the setting was restored.
- Brave YouTube Music artwork, metadata, timeline and routed controls were present. Physical Play/Pause routed
  only to the priority-rule target while ordinary Brave YouTube remained unchanged.
- Lock/unlock, sleep/resume and a YouTube Music reload recovered the intended target and routed each physical
  key operation once without remaining Unavailable.
- Close-to-tray, tray restore and explicit Exit passed. Exit left zero processes and no startup entry.
- `settings.json`, `state.json` and one bounded JSONL log parsed successfully with zero invalid lines and zero
  Error/Critical entries.

## Windows Sandbox gate

Status: pending.

Transfer only the ZIP, manifest and checksum into a fresh Windows Sandbox. Verify:

1. Digest, clean source, packaging flags, one executable, ProductVersion/FileVersion and unsigned status.
2. Cold start without a separately installed .NET runtime, one process/icon and second-instance activation.
3. About/diagnostics, Settings persistence and valid user files.
4. Reversible current-user login startup with the exact extracted executable path plus `--startup`.
5. Edge or Chrome GSMTC discovery, one routed Play/Pause and one supported Seek.
6. Close-to-tray, restore and explicit Exit with zero remaining processes and no startup entry.

Record the Windows caption, display version, full build, architecture and observed results here.

## Public publication

Status: not authorized. Tagging, GitHub Release creation and public ZIP upload require separate approval after both
runtime gates pass for this exact commit and digest.
