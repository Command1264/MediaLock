# Phase 13 `0.3.0-rc.1` release-candidate plan

## Outcome

Produce a reviewable `0.3.0-rc.1` candidate that consolidates the completed Playback State Lock, installable Windows
package and accepted publish-footprint optimization. Phase 13A defines the scope and gates only. Phase 13B performs
the version change, script and documentation updates, formal artifact build and exact-artifact validation after
separate approval.

Phase 13B implementation was approved on 2026-08-25. Push, PR, merge, tag, GitHub Prerelease and public artifact
upload remain separate remote operations.

The public stable `0.2.0` Release and retained `release/0.2` branch remain frozen. Candidate work starts from `develop`;
it does not create `release/0.3`, because a prerelease is not yet the stable hotfix baseline.

## Candidate scope

`0.3.0-rc.1` includes only behavior already completed and integrated before Phase 13A:

- Phase 11A one-way Playback State Lock with Off／Keep Playing, Windows lock-screen override and repeated-pause escape;
- Phase 12A per-user Inno Setup package beside the portable ZIP, including stable install path, Search／Start Menu
  discovery, exact login-startup ownership, in-place upgrade, downgrade block, uninstall and retained user data; and
- Phase 12B compressed self-contained single-file payload with all framework language resources retained and no
  separately installed .NET runtime requirement.

The candidate carries the Phase 11B **Limit** decision unchanged. It does not ship the probe, create a production
mirror, promise to set Windows Current Session or claim that Media Lock's card remains first in Windows. It also does
not add browser URL matching, automatic updates, signing, ARM64, MSI／MSIX, framework-dependent packaging, trimming,
customizable shortcuts or volume control.

No unrelated product feature enters the candidate after this scope freeze. A required candidate fix may be accepted
only when it protects an included contract and receives its own implementation approval and regression coverage.

## Phase 13B implementation slices

### 1. Version and release metadata

- Set product/package version to `0.3.0-rc.1`; keep Windows `FileVersion` at `0.3.0.0`.
- Add candidate release notes that distinguish new user-facing behavior, distribution options, unsigned status,
  upgrade behavior and the Windows media-surface limitation.
- Update README, installation guidance, release runbook, roadmap and testing references together. Until publication,
  wording must call the candidate local/unpublished and keep `0.2.0` as the public Stable／Latest download.
- Preserve the existing statement that manifest and checksum outputs are provenance helpers, not public assets.

### 2. Prerelease upgrade automation

Extend `tests\packaging\WindowsSandbox-InstallerUpgradeSmoke.ps1` so the caller explicitly identifies the previous
stable artifact and newer candidate artifact. Do not pass `0.3.0-rc.1` through a `[version]` cast or infer roles from
two stable manifests. Parse and compare the same supported release-version grammar used by packaging, then prove:

1. settings and state created by the public portable `0.2.0` remain readable after installing `0.3.0-rc.1`;
2. exactly one Installed apps entry, one Start Menu shortcut and the stable install path remain;
3. the installed payload and ProductVersion identify `0.3.0-rc.1`;
4. running the generated older test installer is rejected with exit code 7 without changing payload, registration,
   startup or data;
5. cancellation on the Ready page leaves the older installation unchanged; and
6. a portable `0.2.0` copy can be exited and replaced operationally without the installer deleting its non-owned
   startup value.

Use generated test-only versions for the installer-to-installer transition because `0.2.0` did not publish a Setup.
Separately repeat the real public portable `0.2.0` data/state compatibility path interactively in Windows Sandbox
against the exact formal candidate artifacts.

### 3. Automated release gate

From a clean reviewed commit, run:

```powershell
dotnet restore MediaLock.sln
dotnet format MediaLock.sln --verify-no-changes --no-restore
dotnet test MediaLock.sln --configuration Release --no-restore
dotnet build MediaLock.sln --configuration Release --no-restore
& .\tests\packaging\Publish-ReleaseCandidate.Tests.ps1
& .\tests\packaging\InstallerArtifactSelection.Tests.ps1
& .\tests\packaging\Measure-PublishFootprint.Tests.ps1
```

Also run repository Markdown relative-link validation and `git diff --check`. A relevant failure is a release blocker;
GitHub Actions capacity is not assumed or substituted for these local results.

### 4. Formal artifact build and inspection

After all candidate changes and fixes are reviewed and committed, run exactly once from a clean Worktree:

```powershell
& .\eng\Publish-ReleaseCandidate.ps1 -Version 0.3.0-rc.1
```

Require one staged `MediaLock.exe` payload shared by:

- `MediaLock-0.3.0-rc.1-win-x64.zip`; and
- `MediaLock-Setup-0.3.0-rc.1-win-x64.exe`.

The ZIP contains exactly one file. Manifest schema, source commit, clean-source state, RID, self-contained/single-file
flags, compression flag, product/file versions, payload identity, container sizes and independently recomputed hashes
must all agree. Record `signed: false` for both executable and installer and verify their Authenticode status as
unsigned.

### 5. Exact-artifact host gate

On the i7-8700 reference host, verify the formal candidate rather than a development build:

- cold launch, visible main window, Tray and second-instance restore;
- Windows／English／Traditional Chinese and Light／Dark／Windows theme surfaces;
- Settings save/cancel, startup enable/disable and privacy-safe diagnostics;
- all four Routing Modes, Play/Pause/Next/Previous/Stop and routed Seek;
- Keep Playing correction, Media Lock command override, repeated-pause escape and lock-screen override;
- competing YouTube Music／ordinary YouTube isolation, Recovery, lock/unlock and sleep/resume; and
- explicit Exit, readable settings/state/log JSON and no Error／Critical entry.

Compare ordinary startup feel with `0.2.0` and preserve measured startup evidence only if publish settings differ from
the already accepted Phase 12B profile. Do not claim a new performance result from subjective observation alone.

### 6. Clean Windows Sandbox gate

On a fresh supported Windows 11 x64 Sandbox, preserve Windows edition/build and exact artifact hashes, then verify:

- portable cold start with no separately installed .NET runtime;
- per-user install without UAC, Windows Search／Start Menu discovery and one Installed apps entry;
- default-disabled login startup, enable behavior and exact installed-path command;
- Edge GSMTC discovery plus one routed command and one Seek where supported;
- real portable `0.2.0` to `0.3.0-rc.1` settings/state compatibility, plus generated predecessor-installer transition
  behavior;
- same-version repair, intentional older-version block and Ready-page cancellation boundaries;
- uninstall cleanup, owned versus portable startup preservation and retained user data; and
- final zero process count, no Tray residue, valid JSON/logs and no Error／Critical entry.

Windows Sandbox terminates its environment on sign-out. When that prevents a real sign-out／sign-in check, record the
row as unavailable in Sandbox and run it on a persistent supported Windows host with the exact same reviewed artifact;
the host result may close only this environment-impossible lifecycle row. Other host evidence does not replace Sandbox
evidence, and earlier Phase 11／12 artifacts do not transfer to a different source commit or digest.

## Review and publication boundaries

After Phase 13B gates pass, review the complete diff against repository standards and this plan. Critical／High
findings block integration. Record evidence in follow-up documentation commits without rebuilding or relabeling the
artifact; its manifest source commit remains the immutable provenance point. Push, PR, merge and task-branch cleanup
require explicit authorization for that task.

Tagging and publication are later, separate remote writes. If approved, create a signed annotated
`v0.3.0-rc.1` tag at the exact manifest source commit after confirming that commit is integrated into `develop`, then
create a GitHub **Prerelease**, not Latest. The tag need not point at a later merge/evidence commit. Publicly upload only:

- `MediaLock-0.3.0-rc.1-win-x64.zip`; and
- `MediaLock-Setup-0.3.0-rc.1-win-x64.exe`.

Put both SHA-256 values, exact source commit, unsigned warning, system requirements, installation/portable choice and
known Phase 11B limitation in the Release body. Keep the manifest and standalone `.sha256` files in trusted local
release evidence; do not upload them unless a later explicit publication decision changes this policy.

## Stable follow-up

Feedback and candidate fixes proceed through later RCs without broadening `0.3.0` scope. Promote to stable only after
the final candidate's exact behavior and artifacts pass fresh gates. At that point create and retain `release/0.3` as
the new stable hotfix baseline; do not remove `release/0.2` before the new branch is verified and established under the
repository's release-branch retention policy.
