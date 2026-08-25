# Phase 14B `0.3.0` stable packaged validation

Do not copy identity or runtime results from `0.3.0-rc.1`. Fill this record only from the exact stable artifact
produced after its source commit is reviewed and clean.

## Release identity

Status: final provenance-clean local formal artifact created after the approved settings-synchronization stable
blocker and stale Settings copy fixes; the fresh Windows Sandbox matrix remains pending.

- Version: `0.3.0`.
- Source commit: `7273165234a418d1334fc2075adc7e876db89db2`.
- Source dirty: `false`.
- Runtime identifier: `win-x64`.
- Self-contained: must be `true`.
- Single-file: must be `true`.
- Single-file compressed: must be `true`.
- Signed: `false`; payload and Setup both report Authenticode `NotSigned`.
- Archive: `MediaLock-0.3.0-win-x64.zip`.
- Installer: `MediaLock-Setup-0.3.0-win-x64.exe`.
- Archive SHA-256: `c85869fe7232275a4c651447e8db51614bc536a9cc7cdf5d0011fb50481d557e`.
- Installer SHA-256: `dc610ed0d6c7b0f09f94e98443652f3a75bf8ac16348ed37a872a71bf731668b`.
- Shared payload SHA-256: `cff2031f712731123622416e637fac2695192bbcf78d112e2e43c9527ce4bd5b`.
- Archive size: 76,501,806 bytes.
- Installer size: 76,912,550 bytes.
- Payload size: 82,316,731 bytes.

Independent inspection matched both container manifests and checksum files, matched the shared payload hash and found
exactly one extracted `MediaLock.exe`. Payload and Setup report ProductVersion `0.3.0`, FileVersion `0.3.0.0` and
Authenticode `NotSigned`.

## Automated gate and review

Status: automated gate passed on replacement artifact-source commit
`cb1817e225f888d6397ce61c4a398b6db1c6018c`; the settings-synchronization blocker diff passed the final two-axis
review with zero findings after every review finding was closed.

Record restore, format, complete tests, Release build, all packaging scripts, Markdown relative-link validation,
`git diff --check` and the two-axis Standards／Spec review against the Phase 14 plan. GitHub Actions capacity is not
assumed and no RC test count or result transfers.

Observed RED → GREEN sequence:

- The stable publish contract first failed against the unchanged RC1 project with `MediaLock.App Version must be
  0.3.0`; changing only Version/InformationalVersion made the same contract build and validate stable ZIP／Setup.
- The exact-predecessor contract first failed because the Sandbox scripts did not require a pinned older-installer
  SHA-256. The shared helper now rejects manifest mismatch and pinned-digest mismatch in PowerShell 7 and Windows
  PowerShell 5.1.
- The repair contract first failed because the upgrade script did not execute the newer installer a second time. The
  script now performs stable same-version repair and reports repair exit code plus settings/state invariants.
- The transaction-integrity contract then failed because blocked downgrade and Ready-page cancellation did not report
  a payload invariant. Both paths now compare installed EXE and shortcut SHA-256 values, complete uninstall
  registration, startup and user-data snapshots; cancellation persists and validates its pre-action snapshot.

Gate results on `c1908621ba7ca31db0c1958c4bc8bfa47c327e86`:

- restore and format verification passed;
- 351/351 Release tests passed;
- Release build completed with zero warnings and zero errors;
- publish, artifact-selection and footprint packaging contracts passed; and
- the artifact-selection contract independently passed in PowerShell 7 and Windows PowerShell 5.1.

The Standards and Spec reviews both reported zero findings. Their follow-up checks specifically closed the earlier
payload-invariant and duplicated-assertion risks in the downgrade and cancellation transactions.

The first formal artifact from `3a014accfbfcdd8f793ee1cf6d85fe94e5ff0ec1` was invalidated before publication
after exact-host use revealed that saved Priority Rules were not sent to the running Router. RED coverage reproduced
the stale Brave target while persisted rules preferred Chrome. The fix now applies Router-owned settings immediately,
replaces an active Recovery deadline when its timeout changes, uses the current Fallback Policy, publishes target and
Playback State Lock atomically, and rolls back durable／platform／presentation state when a consumer fails.

The complete automated gate was repeated successfully on replacement artifact-source commit
`cb1817e225f888d6397ce61c4a398b6db1c6018c` before its formal artifacts were built from a clean worktree:

- 360/360 Release tests passed;
- Release build completed with zero warnings and zero errors;
- publish, artifact-selection and footprint packaging contracts passed;
- the artifact-selection contract passed in PowerShell 7 and Windows PowerShell 5.1; and
- Markdown relative-link validation plus `git diff --check` passed.

Exact-host verification then found stale bilingual Settings copy which still claimed Recovery and Priority Rule
changes required restart even though the runtime contract now applies them on Save. Commit
`7273165234a418d1334fc2075adc7e876db89db2` updates both resources and adds a bilingual contract test which rejects
the obsolete restart wording. A pre-existing Seek test was also constrained to its actual one-tick precision instead
of requiring impossible sub-tick equality. The complete Release gate was repeated before rebuilding the formal
artifacts:

- 362/362 Release tests passed;
- Release build completed with zero warnings and zero errors;
- format verification, the publish packaging contract and `git diff --check` passed; and
- the formal ZIP and Setup were built from a clean worktree at the exact source commit recorded above.

## Exact-artifact host gate

Status: blocker regression, full functional host matrix and final exact-artifact smoke passed.

The exact ZIP payload from `cb1817e225f888d6397ce61c4a398b6db1c6018c` launched as one process. With Priority
Rules already active and Brave PWA initially first, Settings moved Chrome to the first rule and saved. Without restart
or reselecting Priority Rules, the background accessibility projection immediately changed Current media target to
`Chrome — 回不去的夏天`. This closes the reported runtime settings-synchronization blocker without relying on the
persisted JSON alone.

The same replacement candidate completed the four Routing Modes, Play/Pause/Next/Previous/Stop, Seek, competing
source isolation, Playback State Lock external-pause recovery and three-pause escape, Recovery, lock/unlock and
sleep/resume checks. Sleep/resume intentionally disables Keep Playing and does not restart audio without user input.
Close-to-Tray, second-instance activation and Tray Exit also passed. No Error/Critical log entry or crash was found.

The final exact ZIP payload from `7273165234a418d1334fc2075adc7e876db89db2` then cold-started as one process,
displayed stable version `0.3.0`, and exposed the corrected Traditional Chinese Settings text stating that Recovery
and Priority Rule changes apply immediately after Save. The English wording is covered by the same bilingual resource
contract. Cancelling the inspection preserved `language=system`, `theme=dark` and disabled login startup. A second
launch retained one process, closing the main window retained the Tray process, and relaunching restored the same
window. The final process was then stopped after inspection; no product behavior changed after the functional host
matrix other than the verified localized copy.

The remaining release blocker is the fresh Windows Sandbox matrix below, using only the final formal ZIP and Setup.

## Windows Sandbox gate

Status: pending on a fresh Windows 11 x64 Sandbox.

Record artifact identity, portable launch, per-user install, Search/Start Menu and Installed apps registration,
startup ownership, `MSEdge` GSMTC routing/Seek, real public portable `0.2.0` data compatibility, exact public RC1 Setup
upgrade to stable using the pinned published Setup SHA-256, stable repair with unchanged settings/state, RC1 downgrade
block with all transaction state unchanged, Ready-page cancellation with all preparation state unchanged, uninstall
retention/cleanup and final process/log state. Real sign-out/sign-in is host-only because Sandbox destroys its
environment on sign-out.

## Integration and publication

Status: not started; requires separate approval after every gate above passes.

Record the retained `release/0.3` baseline, PRs into `develop` and `main`, signed annotated tag, public Stable／Latest
Release, public ZIP/Setup assets and independently downloaded GitHub digests only after those operations occur. Never
alter the historical `v0.2.0` or `v0.3.0-rc.1` assets, and retain `release/0.2` throughout Phase 14.
