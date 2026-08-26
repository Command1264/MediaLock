# Phase 14B `0.3.0` stable packaged validation

Do not copy identity or runtime results from `0.3.0-rc.1`. Fill this record only from the exact stable artifact
produced after its source commit is reviewed and clean.

## Release identity

Status: final provenance-clean local formal artifact created after the approved settings-synchronization, stale
Settings copy and login-startup self-repair fixes; the targeted host and fresh Windows Sandbox replacement gates passed.

- Version: `0.3.0`.
- Source commit: `a773fac983728f5d4b2d8cbe40bfad9d1c016737`.
- Source dirty: `false`.
- Runtime identifier: `win-x64`.
- Self-contained: must be `true`.
- Single-file: must be `true`.
- Single-file compressed: must be `true`.
- Signed: `false`; payload and Setup both report Authenticode `NotSigned`.
- Archive: `MediaLock-0.3.0-win-x64.zip`.
- Installer: `MediaLock-Setup-0.3.0-win-x64.exe`.
- Archive SHA-256: `94deac66e195cfca21c826ee86f3e61097f3440d3a84ebb5af2439ef3ad3d437`.
- Installer SHA-256: `7937f807b2ec577b88d3506735dfb7c26fe0c12b0f055a2b739364bb9ab4d00d`.
- Shared payload SHA-256: `07bdb1f281333df5cb43b2d0a6bb74daf881a9a15419389f15583ca2d27f4a02`.
- Archive size: 76,504,983 bytes.
- Installer size: 76,915,357 bytes.
- Payload size: 82,319,809 bytes.

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
- the superseded ZIP and Setup were built from a clean worktree at that exact source commit.

The first host sign-out/sign-in attempt then exposed a stale `Run` value owned by an unavailable historical portable
path after the installed `0.3.0` process had launched. The `7273165` artifacts were preserved as superseded and were
not published. RED Application coverage reproduced a platform value changing after initial synchronization; Windows
adapter coverage exercises the real Registry notification. Commit
`a773fac983728f5d4b2d8cbe40bfad9d1c016737` monitors the Run key with `RegNotifyChangeKeyValue`, reconciles changes
through the serialized settings boundary and retains the disabled-setting rule that does not delete another portable
owner. Its clean gate passed 365/365 Release tests, format verification, a zero-warning／zero-error Release build, the
publish packaging contract and `git diff --check` before the final artifacts recorded above were built.

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

The superseded exact ZIP payload from `7273165234a418d1334fc2075adc7e876db89db2` then cold-started as one process,
displayed stable version `0.3.0`, and exposed the corrected Traditional Chinese Settings text stating that Recovery
and Priority Rule changes apply immediately after Save. The English wording is covered by the same bilingual resource
contract. Cancelling the inspection preserved `language=system`, `theme=dark` and disabled login startup. A second
launch retained one process, closing the main window retained the Tray process, and relaunching restored the same
window. That process was stopped after inspection.

The final exact Setup from `a773fac983728f5d4b2d8cbe40bfad9d1c016737` repaired the same-version host install.
Before launch, the Run value intentionally remained the stale historical portable path; the installed primary process
repaired it to `"%LocalAppData%\Programs\MediaLock\MediaLock.exe" --startup` while retaining one process and the
persisted enabled preference. A real sign-out/sign-in then started that exact installed payload without manual launch.
The notification-area icon appeared, process path and command line matched the installed executable, the Run value
remained correct, settings/state were valid and two rotated JSONL files contained zero invalid or Error／Critical lines.

## Windows Sandbox gate

Status: targeted replacement gate passed on a fresh Windows 11 Enterprise 24H2 x64 Sandbox, build `26100.9168`.

The mapped final formal artifacts independently matched source commit
`a773fac983728f5d4b2d8cbe40bfad9d1c016737`, the archive and installer digests recorded above, ProductVersion
`0.3.0`, FileVersion `0.3.0.0` and Authenticode `NotSigned`. The self-contained single-file portable payload launched
without installing .NET, and a second launch retained one process.

The replacement automated installer transactions passed with these exact results:

- default login startup remained disabled;
- uninstall removed the owned startup value but preserved an unrelated portable startup value;
- user data was retained and the final installer-smoke process count was zero;
- public `0.3.0-rc.1` Setup SHA-256
  `0ec8c554e7eb7ceb9e7857e07ed1388babc7b70ff42ca1e24684b064c740d2c3` upgraded to stable with exit code 0;
- same-version stable repair returned exit code 0 and preserved payload, registration, shortcut, settings, state and
  startup command;
- stable-to-RC1 downgrade was blocked with exit code 7 and every captured transaction invariant stayed unchanged.

The broader superseded `7273165` Sandbox matrix additionally checked public portable `v0.2.0` data compatibility,
Windows Search, Ready-page cancellation and Edge GSMTC behavior. Public `v0.2.0` wrote `English`, `Light`, a 17-second
Recovery timeout and disabled login startup; its stable install preserved all four values and valid settings/state.
Cancellation returned exit code 2 without changing the RC1 installation. A second clean Sandbox visibly returned
Media Lock as the best Windows Search application result.

Edge exposed one `MSEdge` session with title, artwork and timeline. Lock session, Pause and Play each succeeded once.
The advertisement correctly rejected Seek and produced a dismissible actionable notice; after skipping it, one
release-only Seek moved the actual song from approximately `0:18` to `2:25`. Final uninstall left zero Media Lock
processes, no installed payload, shortcut, Installed apps entry or startup value. Settings/state remained valid, and
the one JSONL log contained zero invalid lines and zero Error/Critical entries.

The replacement Sandbox additionally installed the exact final Setup, wrote a valid enabled schema-v7 setting and
replaced its Run value with `"C:\DoesNotExist\MediaLock.exe" --startup`. Launching the installed primary repaired the
value to its own executable, retained one matching process and produced no competing owner. Final uninstall returned
zero, removed the installed payload and removed the now-owned startup value. Real sign-out/sign-in remains host-only
because Sandbox destroys its environment on sign-out; that exact host row passed as recorded above.

## Integration and publication

Status: integration complete; publication not started and still requires separate approval.

PR #46 merged the reviewed stable source and evidence into `develop` as
`06aafe39fc16940f5d7c9e8ac453f6e1d6ae875d`. PR #47 then synchronized `develop` into `main` as
`966dce2f2ef0389fc372d39189bc718ec0cace4e`. The retained `release/0.3` hotfix baseline points to exact artifact
source `a773fac983728f5d4b2d8cbe40bfad9d1c016737`; the existing `release/0.2` branch and Worktree remain retained.

No `v0.3.0` tag, GitHub Stable／Latest Release or public ZIP/Setup upload has been created. Record the signed annotated
tag, public assets and independently downloaded GitHub digests only after those separately authorized operations occur.
Never alter the historical `v0.2.0` or `v0.3.0-rc.1` assets.
