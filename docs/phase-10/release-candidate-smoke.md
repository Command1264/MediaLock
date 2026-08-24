# Phase 10C `0.2.0-rc.3` packaged validation

Do not copy values or results from an earlier candidate. Fill this record only from the exact formal artifact produced
after the release source commit is reviewed and clean.

## Candidate identity

- Version: `0.2.0-rc.3`.
- Source commit: `0431eeedd6858901c0e9e189fa3344d3fa2455a9`.
- Source dirty: `false`.
- Runtime identifier: `win-x64`.
- Self-contained: must be `true`.
- Single-file: must be `true`.
- Signed: expected `false`; verify Authenticode independently.
- Archive: `MediaLock-0.2.0-rc.3-win-x64.zip`; 78,676,024 bytes.
- Archive SHA-256: `b8e6c0b7b8dd734ec7ab5b7a811ee786a8181f43322bf38f36ac0bc06a1bf157`.

Before host testing, independently recompute the digest, compare the manifest and checksum, expand exactly one
`MediaLock.exe`, and record ProductVersion, FileVersion and Authenticode status.

## Local host smoke

Status: passed on Windows 11 Pro 25H2, build `26200.9168`, 64-bit, for the exact commit and digest above.

The host used an ASUS ROG STRIX FLARE keyboard, a regular Brave YouTube source and the Brave YouTube Music PWA.
Priority Rules resolved YouTube Music while the competing regular YouTube source remained available.

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

Observed results:

- The independently recomputed archive digest matched both the manifest and checksum. Extraction produced exactly one
  200,300,136-byte `MediaLock.exe`; ProductVersion was `0.2.0-rc.3`, FileVersion was `0.2.0.0`, and Authenticode was
  `NotSigned`.
- Cold start, the notification icon, one-process enforcement and second-instance activation succeeded. About reported
  the candidate version, host build, architecture, prerelease status and unsigned state.
- Copied diagnostics used 12 CRLF separators with no lone LF or CR. The expected support facts were present while media
  title, artist, source application, account name, private paths, settings payload, persisted identity and source commit
  were absent. Open logs, Open support and Report a bug reached their intended destinations without submitting data.
- Settings persisted successfully. Enabling login startup through a normally launched candidate created the exact
  `"<executable>" --startup` current-user Run value without elevation; disabling it removed the value. An earlier
  automation-broker launch did not expose the same Registry behavior, so the release result was established with a
  normal Windows process launch and independently verified Registry values.
- Sessions, artwork, timeline and routed Play/Pause worked. One physical Play/Pause changed YouTube Music exactly once
  while the regular YouTube source remained unchanged.
- Close-to-tray, notification-area restore and explicit Exit succeeded; the final process count was zero and the
  disabled login-startup value remained absent.
- `settings.json` and `state.json` parsed successfully. One JSONL log file contained zero invalid lines and zero
  Error/Critical or failure events.

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
