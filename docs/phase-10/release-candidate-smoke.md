# Phase 10C `0.2.0-rc.3` packaged validation

Do not copy values or results from an earlier candidate. Fill this record only from the exact formal artifact produced
after the release source commit is reviewed and clean.

## Candidate identity

- Version: `0.2.0-rc.3`.
- Source commit: pending.
- Source dirty: must be `false`.
- Runtime identifier: `win-x64`.
- Self-contained: must be `true`.
- Single-file: must be `true`.
- Signed: expected `false`; verify Authenticode independently.
- Archive: `MediaLock-0.2.0-rc.3-win-x64.zip`; size pending.
- Archive SHA-256: pending.

Before host testing, independently recompute the digest, compare the manifest and checksum, expand exactly one
`MediaLock.exe`, and record ProductVersion, FileVersion and Authenticode status.

## Local host smoke

Status: pending for the exact commit and digest above.

Record the host Windows caption/display version/full build/architecture, keyboard model, named GSMTC sources and
Routing Mode. Verify:

1. Cold start, one process/icon and second-instance activation.
2. About facts show `0.2.0-rc.3`, the real Windows build and unsigned prerelease state.
3. Copy diagnostics uses CRLF and excludes current media title, artist, account name, full path, complete settings and
   persisted target identity; Open logs, Open support and Report a bug reach the expected targets without saving or
   closing Settings.
4. A reversible Settings change persists; login startup enables and disables without elevation.
5. Named Sessions, artwork, timeline, routed controls and Playing/Paused Seek remain correct.
6. One physical Play/Pause affects only the resolved target while a competing source remains unchanged.
7. Close-to-tray, restore and explicit Exit leave no orphan process.
8. `settings.json`, `state.json` and bounded `logs\*.jsonl` remain parseable with no unexpected Error/Critical entry.

## Windows Sandbox gate

Status: pending for the exact commit and digest above.

Transfer only the ZIP, manifest and checksum into a fresh Windows Sandbox. Record Windows caption/display version/full
build/architecture and verify:

1. SHA-256, manifest source commit, `sourceDirty: false`, package flags and exactly one executable.
2. ProductVersion `0.2.0-rc.3`, FileVersion `0.2.0.0` and Authenticode `NotSigned`.
3. Cold start without a separately installed .NET runtime, default language/theme/routing/interception and one process.
4. About facts and privacy-safe diagnostics; Settings save/persistence and valid user files.
5. Reversible current-user login startup with the exact extracted executable path plus `--startup`.
6. Edge Session discovery as `MSEdge`, Session Lock, Play/Pause and one supported Seek while another source is unchanged
   when the environment exposes one.
7. Close-to-tray, notification-area restore and explicit Exit with zero remaining processes.

No candidate may be called portable or published until both sections are completed for the same commit and digest.

## Public publication

Status: not authorized. Do not create a tag, GitHub Release or public asset from this record alone.
