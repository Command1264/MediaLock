# Phase 13B `0.3.0-rc.1` packaged validation

Do not copy values or results from an earlier version. Fill this record only from the exact formal artifacts produced
after the candidate source commit is reviewed and clean.

## Candidate identity

Status: second replacement artifacts built and independently inspected. Earlier `25690bf` and `229c9eb` artifacts are
retained only as superseded provenance and are not publishable candidates.

- Version: `0.3.0-rc.1`.
- Source commit: `d0fe5583e91204fe98a79b14ae0327e5120af54e`.
- Source dirty: `false`.
- Runtime identifier: `win-x64`.
- Self-contained: `true`.
- Single-file: `true`.
- Single-file compressed: `true`.
- ZIP: `MediaLock-0.3.0-rc.1-win-x64.zip`; 76,500,560 bytes;
  SHA-256 `bfcbc61998173c036ce1beb7574013920a906f6a01d2f1df1599791d24e29066`.
- Setup: `MediaLock-Setup-0.3.0-rc.1-win-x64.exe`; 76,911,036 bytes;
  SHA-256 `0ec8c554e7eb7ceb9e7857e07ed1388babc7b70ff42ca1e24684b064c740d2c3`.
- Payload: `MediaLock.exe`; 82,315,358 bytes;
  SHA-256 `c2bbde78f195701356d9ffa2f23d68070fcc814cd448672168ee02a5119c0339`.
- ProductVersion: `0.3.0-rc.1` for payload and Setup.
- FileVersion: `0.3.0.0` for payload and Setup.
- Executable and Setup signatures: independently verified `NotSigned`.

Both recomputed container digests matched manifest schema 3 and their standalone checksum files. The expanded ZIP
contained exactly one `MediaLock.exe`; its recomputed digest matched the manifest payload digest. Manifest RID,
self-contained, single-file, compression, trimming and signing fields matched the planned contract.

## Automated gate

Status: passed on 2026-08-25 against exact source commit
`d0fe5583e91204fe98a79b14ae0327e5120af54e`.

- Restore and formatting verification passed.
- All 351 Release tests passed; Release build completed with 0 warnings and 0 errors.
- Publish and footprint contract tests passed, including PowerShell 7 and Windows PowerShell 5.1 prerelease artifact
  selection.
- ZIP／Setup one-payload identity, manifest schema, hashes, unsigned state and clean-source guards passed.
- Markdown relative links and `git diff --check` passed.
- Standards review ended with 0 findings. Spec review found one missing post-downgrade startup assertion; commit
  `25690bf` added a fresh Registry read and ordinal full-command comparison, after which focused re-review ended with
  0 findings.

## Local host smoke

Status: in progress. Exact second replacement payload from source `d0fe558` launched as one process on the local host.
The 2026-08-25 real sleep row passed: entering sleep turned Keep Playing Off; after wake YouTube Music remained paused,
Priority Rules recovered, ordinary YouTube did not change, and the application neither remained Unavailable nor
reported an error or crash. A subsequent Priority Rules YouTube Music reload entered Recovering, reacquired the target,
retained Keep Playing, resumed playback without changing ordinary YouTube and produced no Unavailable, error or crash.
An ordinary workstation Lock／Unlock without lock-screen media-card input also retained Keep Playing and Priority Rules,
kept YouTube Music playing, did not change ordinary YouTube and produced no Unavailable, error or crash. The remaining
exact-artifact host rows are pending. With ordinary YouTube in the foreground, one physical Play/Pause press routed
exactly one Pause to YouTube Music, turned Keep Playing Off, left Priority Rules selected, did not change ordinary
YouTube and produced no error or crash. The default repeated-pause override also passed: the first two direct player UI
pauses were corrected, the third pause within five seconds remained paused, Keep Playing turned Off, one notification
sound played, ordinary YouTube did not change and no error or crash occurred. A lock-screen media-card Pause also left
YouTube Music paused, turned Keep Playing Off, recovered Priority Rules after unlock, did not change ordinary YouTube
and produced no Unavailable, error or crash. The four-mode selection indicator, stable button sizing, one-shot
Play／Pause／Toggle／Next／Previous／Stop, release-only Seek and competing-source isolation all passed. Windows Auto
Keep Playing also behaved as specified: with two playing Sessions, Windows promoted the competitor after the armed
target paused, so the Active Target change safely ended protection; with only one Session, Keep Playing remained usable.

The first replacement artifact from source `229c9eb` passed the controlled Priority Rules reload and three consecutive
same-target Session Lock reloads, plus ordinary Lock／Unlock. The subsequent real sleep row exposed an intentional
product-policy decision: waking left the player paused and Keep Playing off. The accepted policy now treats Power
Suspend as a safety boundary and forbids automatic audio resume. Source `229c9eb` and its artifacts are therefore also
superseded; a second replacement build and exact-artifact rerun are required.

Record Windows build, i7-8700 host identity, ASUS ROG STRIX FLARE keyboard, named YouTube Music and ordinary YouTube
sources, Routing Mode, exact candidate source commit and both container hashes. Verify:

1. Cold launch, one process/icon, second-instance restore, Settings and Tray Exit.
2. Windows／English／Traditional Chinese, Windows／Light／Dark, About and privacy-safe diagnostics.
3. Four Routing Modes, Play/Pause/Next/Previous/Stop, routed Seek and competing-source isolation.
4. Keep Playing correction, Media Lock command override, repeated-pause escape and Windows lock-screen override.
5. Recovery, lock/unlock and sleep/resume without duplicate routing or a stuck Unavailable state.
6. Startup enable/disable, readable settings/state/log JSON and zero unexpected Error／Critical lines.
7. Ordinary launch responsiveness remains consistent with the accepted Phase 12B profile; no new performance claim is
   made unless the publish profile changes and the quantitative benchmark is repeated.

## Windows Sandbox gate

Status: pending.

Record Windows edition/display version/full build/architecture and the exact candidate hashes. Verify:

1. ZIP and Setup hashes, manifest fields, one-file extraction, ProductVersion/FileVersion and unsigned state.
2. Portable cold start without a separately installed .NET runtime.
3. Per-user Setup without UAC, fixed path, Search／Start Menu, Installed apps and single-instance behavior.
4. Startup disabled by default; enabling and actual relogin use the exact installed path.
5. Settings/state created by the real public portable `0.2.0` remain readable after installing the candidate.
6. Generated predecessor Setup upgrades in place to `0.3.0-rc.1`; the older installer is blocked with exit code 7,
   same-version repair is allowed, and Ready-page cancellation preserves the installed version/data/startup command.
7. Edge appears as `MSEdge`; one routed command and supported Seek work without a competing source changing.
8. Uninstall removes installed files/registration and only an installer-owned startup value while retaining user data
   and a portable-owned startup value.
9. Final process count is zero; settings/state/log JSON remain valid with no unexpected Error／Critical entry.

Host evidence does not replace Sandbox evidence. Record inherited or unavailable hardware rows explicitly instead of
silently treating them as passed.

## Publication

Status: not authorized and not started.

After the exact manifest source commit is integrated into `develop`, a separate approval is required for signed
annotated tag `v0.3.0-rc.1`, GitHub Prerelease creation and public ZIP／Setup upload. Do not upload manifest or standalone
checksum files under the current publication policy, and do not mark the prerelease Latest.
