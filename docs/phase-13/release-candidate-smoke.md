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

Status: passed on Windows 11 Pro 25H2 build `26200.9168` x64, Intel Core i7-8700 and an ASUS ROG STRIX FLARE
keyboard. The exact second replacement payload from source `d0fe558` launched as one process with Brave YouTube Music
and ordinary Brave YouTube as named competing sources.
The 2026-08-25 real sleep row passed: entering sleep turned Keep Playing Off; after wake YouTube Music remained paused,
Priority Rules recovered, ordinary YouTube did not change, and the application neither remained Unavailable nor
reported an error or crash. A subsequent Priority Rules YouTube Music reload entered Recovering, reacquired the target,
retained Keep Playing, resumed playback without changing ordinary YouTube and produced no Unavailable, error or crash.
An ordinary workstation Lock／Unlock without lock-screen media-card input also retained Keep Playing and Priority Rules,
kept YouTube Music playing, did not change ordinary YouTube and produced no Unavailable, error or crash. The remaining
exact-artifact host rows also passed. With ordinary YouTube in the foreground, one physical Play/Pause press routed
exactly one Pause to YouTube Music, turned Keep Playing Off, left Priority Rules selected, did not change ordinary
YouTube and produced no error or crash. The default repeated-pause override also passed: the first two direct player UI
pauses were corrected, the third pause within five seconds remained paused, Keep Playing turned Off, one notification
sound played, ordinary YouTube did not change and no error or crash occurred. A lock-screen media-card Pause also left
YouTube Music paused, turned Keep Playing Off, recovered Priority Rules after unlock, did not change ordinary YouTube
and produced no Unavailable, error or crash. The four-mode selection indicator, stable button sizing, one-shot
Play／Pause／Toggle／Next／Previous／Stop, release-only Seek and competing-source isolation all passed. Windows Auto
Keep Playing also behaved as specified: with two playing Sessions, Windows promoted the competitor after the armed
target paused, so the Active Target change safely ended protection; with only one Session, Keep Playing remained usable.
Second launch restored the existing window while retaining one process. English／Light and Traditional Chinese／Dark
applied immediately, Windows language／theme were restored, About reported `0.3.0-rc.1`, and the diagnostics notice
expired automatically. Close-to-Tray, Tray restore and Tray Exit passed. After Exit the process count was zero;
settings and state parsed as JSON, one JSONL log contained zero invalid lines and zero Error／Critical entries, and the
persisted language and theme were both `system`.
Enabling startup persisted `true` and created an ordinally exact Registry command pointing to the candidate executable
with `--startup`. After an actual host sign-out／sign-in, the notification-area icon appeared without a manual launch;
exactly one process ran from the reviewed `d0fe558` payload path at ProductVersion `0.3.0-rc.1`, while the Registry
command and persisted setting remained exact. Disabling startup then persisted `false`, removed the Registry value and
remained unchecked after reopening Settings. Final Tray Exit left zero processes; settings, state and the single JSONL
log remained valid with zero invalid or Error／Critical lines. No qualitative responsiveness regression was observed
during the smoke; the quantitative Phase 12B benchmark was not repeated and no new performance claim is made.

The first replacement artifact from source `229c9eb` passed the controlled Priority Rules reload and three consecutive
same-target Session Lock reloads, plus ordinary Lock／Unlock. The subsequent real sleep row exposed an intentional
product-policy decision: waking left the player paused and Keep Playing off. The accepted policy now treats Power
Suspend as a safety boundary and forbids automatic audio resume. Source `229c9eb` and its artifacts are therefore also
superseded. Source `d0fe558` implemented that policy, and the second replacement artifact completed the exact-artifact
host rerun recorded above.

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

Status: passed with one documented Sandbox lifecycle exception completed on the exact-artifact host. Sandbox ran on
Windows 11 Enterprise 24H2 build `26100.9168` x64 against source
`d0fe5583e91204fe98a79b14ae0327e5120af54e`. The recomputed ZIP, Setup and payload hashes matched the candidate values
above and their standalone checksum files. Manifest identity, clean-source state, self-contained compressed single-file
profile, payload and Setup versions, and unsigned signatures all matched. The archive expanded to one `MediaLock.exe`.

Portable cold start succeeded without a separately installed .NET runtime, a security prompt or an application error.
The second launch restored the existing window and retained one Media Lock process; Tray Exit completed without an
error or crash. The per-user Setup required no UAC, installed to
`%LOCALAPPDATA%\Programs\MediaLock`, created the Windows Search／Start Menu entry and exactly one Installed apps entry,
and left startup disabled by default. Enabling startup persisted `true` and produced the ordinally exact installed-path
command with `--startup`; disabling it removed the value. Windows Sandbox terminates the Sandbox when the user signs
out, so a real sign-out／sign-in startup run was unavailable there. The persistent host completed that otherwise
impossible row using the exact same payload as recorded above; no other host result substituted for Sandbox evidence.

Settings and state created by the actual public portable `0.2.0` remained readable after installing the candidate;
English, Light, a 17-second recovery timeout and disabled startup were preserved. Same-version repair kept one Installed
apps entry and preserved those values. A generated test-only `0.2.9` predecessor upgraded in place to `0.3.0-rc.1`
while preserving settings, state, a retained marker and its startup command. Attempting to downgrade an installed RC1
with the older Setup returned exit code 7. The payload, Installed apps identity/version/install location/uninstall
command, Start Menu shortcut, exact startup command, settings and state all remained unchanged, with zero Media Lock
processes afterward. Cancelling the RC1 Setup on the Ready to Install page returned exit code 2 and likewise preserved
the older installation, data and startup command. The generated `0.2.9` artifacts are test fixtures and are not
publication assets.

Edge appeared as `MSEdge`; title and timeline were correct, Session Lock succeeded, Pause and Play each executed once,
and supported Seek executed once without an error or crash. Uninstall removed the installed executable, Search／Start
Menu registration, Installed apps entry and installer-owned startup value while preserving user data. A separate
portable-owned startup value survived uninstall as required and was then removed as test cleanup. Final state had zero
Media Lock processes and no installed registration or startup value. Settings and state remained valid JSON; the one
JSONL log had zero invalid lines and zero Error／Critical entries. The final `zh-TW`／Dark preference was an intentional
manual accessibility change during the Sandbox run, while the 17-second compatibility value remained intact.

Sandbox does not expose the host keyboard or faithfully reproduce host lock／sleep hardware paths, so those rows inherit
the exact-artifact host evidence above and were not silently counted as independent Sandbox passes.

## Publication

Status: not authorized and not started.

After the exact manifest source commit is integrated into `develop`, a separate approval is required for signed
annotated tag `v0.3.0-rc.1`, GitHub Prerelease creation and public ZIP／Setup upload. Do not upload manifest or standalone
checksum files under the current publication policy, and do not mark the prerelease Latest.
