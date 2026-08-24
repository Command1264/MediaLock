# Phase 10D `0.2.0` stable packaged validation

Do not copy identity or runtime results from a release candidate. Fill this record only from the exact stable artifact
produced after its source commit is reviewed and clean.

## Release identity

- Version: `0.2.0`.
- Source commit: pending.
- Source dirty: must be `false`.
- Runtime identifier: `win-x64`.
- Self-contained: must be `true`.
- Single-file: must be `true`.
- Signed: expected `false`; verify Authenticode independently.
- Archive: `MediaLock-0.2.0-win-x64.zip`.
- Executable: `MediaLock.exe`.
- Archive SHA-256: pending.

Before host testing, independently recompute the digest, compare the manifest and checksum, expand exactly one
`MediaLock.exe`, and record ProductVersion `0.2.0`, FileVersion `0.2.0.0` and Authenticode status.

## Local host smoke

Status: pending.

Use an ASUS ROG STRIX FLARE keyboard, Brave YouTube Music and ordinary Brave YouTube. Verify:

1. Cold start, one process/icon and second-instance activation.
2. About and copied diagnostics show `0.2.0`, Stable and Unsigned with privacy-safe CRLF output.
3. A reversible Settings change persists; login startup enables and disables without elevation.
4. Named Sessions, artwork, timeline, routed controls and Playing/Paused Seek remain correct.
5. One physical Play/Pause affects only the resolved target while the competing source remains unchanged.
6. Recovery, lock/unlock and sleep/resume return to the intended target without duplicate routing.
7. Close-to-tray, restore and explicit Exit leave no orphan process or startup entry.
8. `settings.json`, `state.json` and bounded `logs\*.jsonl` remain parseable with no unexpected Error/Critical entry.

Record Windows build, source commit, digest and observed results here.

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
