# Phase 14 `0.3.0` stable-release plan

## Outcome

Promote the publicly validated `0.3.0-rc.1` feature set to stable `0.3.0` without adding product behavior. Phase 14A
freezes scope and evidence requirements only. Phase 14B performs the version/documentation change, automated gate,
formal artifact build and exact-artifact validation after separate approval. Integration and public release remain
later approval boundaries.

Status: complete and published on 2026-08-26. The retained `release/0.3` baseline and GPG-signed annotated `v0.3.0`
tag identify exact artifact source `a773fac983728f5d4b2d8cbe40bfad9d1c016737`; the GitHub Release is Stable／Latest.

Stable work starts from `develop` after this plan is integrated. The public `v0.2.0` Release, its immutable assets and
the retained `release/0.2` hotfix Worktree/branch remain untouched throughout Phase 14. A verified `release/0.3`
baseline is created only from the accepted stable source commit. After Phase 14 publication and independent public
download verification completed, the separately authorized maintenance step retired `release/0.2` while preserving
its historical tag, GitHub Release, asset and provenance.

## Stable scope

`0.3.0` contains exactly the behavior already shipped in `0.3.0-rc.1`:

- Phase 11A one-way Playback State Lock with Off／Keep Playing, lock-screen and power-suspend safety boundaries, and
  the configurable repeated-pause escape;
- Phase 12A ordinary-user Inno Setup package beside the portable ZIP, including fixed per-user installation,
  Search／Start Menu discovery, exact startup ownership, repair, upgrade, downgrade protection, uninstall and retained
  user data; and
- Phase 12B compressed self-contained single-file payload with all framework language resources and no separately
  installed .NET runtime requirement.

The Phase 11B **Limit** remains a product boundary: stable `0.3.0` does not ship the probe, create a production Windows
Media Surface Mirror, set Windows Current Session or promise that a Media Lock card remains first. Browser URL/tab
matching, automatic updates, signing, ARM64, MSI／MSIX, framework-dependent packaging, trimming, volume/mute and
customizable shortcuts also remain outside this release.

No new feature enters Phase 14. A stable blocker may be fixed only when it protects an included contract, receives its
own implementation approval and adds focused regression coverage. Such a fix invalidates RC1 runtime evidence and the
stable candidate must still pass every fresh gate below. If a fix materially changes user-visible behavior or cannot
be closed with bounded regression coverage, produce another RC instead of silently widening stable scope.

There is no required download count or arbitrary waiting period. Before implementation, check open GitHub issues and
the owner's RC1 observations for unresolved Critical／High defects. The product owner may accept the completed RC1
soak when no release blocker is known; lack of reports is not presented as proof of compatibility.

## Phase 14B implementation slices

### 1. Stable identity and documentation

- Set product/package/InformationalVersion to `0.3.0`; keep Windows `FileVersion` at `0.3.0.0`.
- Add `docs/releases/0.3.0.md` and update README, installation guidance, release runbook, roadmap and testing references
  together. Historical `0.3.0-rc.1` notes, hashes and publication evidence remain immutable.
- Before publication, call `0.3.0` a local or unpublished stable candidate and keep public `v0.2.0` as Stable／Latest.
- Continue identifying the executable and Setup as unsigned. Installer format must not be described as suppressing
  SmartScreen, Smart App Control or reputation warnings.
- Keep public assets limited to the portable ZIP and Setup EXE. Manifest and standalone checksum files remain trusted
  local provenance unless a separately approved publication policy changes.

### 2. Stable-transition automation

Make stable identity and transition expectations fail before changing them, then prove the supported release-version
parser orders public `0.3.0-rc.1` below stable `0.3.0` in both PowerShell 7 and Windows PowerShell 5.1. Do not cast the
prerelease to `[version]` or infer artifact roles from directory order.

The clean-Windows transaction gate must cover:

1. an exact public `0.3.0-rc.1` Setup installation upgrading in place to `0.3.0`;
2. one fixed install path, one Installed apps entry, one Start Menu shortcut and the stable payload/ProductVersion;
3. unchanged settings, state, retained marker and exact installed-path startup command across the upgrade;
4. same-version `0.3.0` repair without duplicate registration or data loss;
5. the public RC1 installer being rejected after stable installation with exit code 7 while payload, registration,
   startup and user data remain unchanged;
6. cancelling stable Setup on the Ready page while RC1 is installed leaves RC1 unchanged; and
7. stable uninstall removes only installed program resources and an owned startup value while retaining user data and
   preserving a portable-owned startup value.

Separately repeat the real public portable `0.2.0` settings/state compatibility path against the formal stable
artifacts. Public `0.2.0` did not contain Setup, so it is not represented as an installer-to-installer predecessor.

### 3. Automated release gate

From the reviewed stable-change Worktree, run:

```powershell
dotnet restore MediaLock.sln
dotnet format MediaLock.sln --verify-no-changes --no-restore
dotnet test MediaLock.sln --configuration Release --no-restore
dotnet build MediaLock.sln --configuration Release --no-restore
& .\tests\packaging\Publish-ReleaseCandidate.Tests.ps1
& .\tests\packaging\InstallerArtifactSelection.Tests.ps1
& .\tests\packaging\Measure-PublishFootprint.Tests.ps1
```

Also run Markdown relative-link validation and `git diff --check`. GitHub Actions capacity is not assumed; these local
checks are the authoritative automated gate. Any relevant failure blocks the stable artifact.

### 4. Review and formal artifact

Run the repository's two-axis Standards／Spec review against this plan. Critical／High findings must be fixed, recorded
or explicitly accepted before integration. After the complete stable diff is reviewed and committed, run exactly once
from a clean Worktree:

```powershell
& .\eng\Publish-ReleaseCandidate.ps1 -Version 0.3.0
```

Require one staged `MediaLock.exe` shared by:

- `MediaLock-0.3.0-win-x64.zip`; and
- `MediaLock-Setup-0.3.0-win-x64.exe`.

The ZIP contains exactly one file. Manifest schema, stable version classification, source commit, clean-source state,
RID, self-contained/single-file/compression flags, ProductVersion/FileVersion, payload identity, container sizes and
independently recomputed hashes must agree. Authenticode remains `NotSigned` for payload and Setup. Evidence commits
may document results later, but must not rebuild or relabel this immutable artifact pair.

### 5. Exact-artifact host gate

On the i7-8700 reference host, test the formal stable artifact rather than a development build:

- cold launch, visible main window/Tray and second-instance restore;
- Windows／English／Traditional Chinese and Light／Dark／Windows theme surfaces;
- Settings save/cancel, startup enable/disable and privacy-safe diagnostics notice lifecycle;
- all four Routing Modes, Play/Pause/Next/Previous/Stop and routed Seek;
- Keep Playing correction, Media Lock command override, repeated-pause escape, lock-screen override and the explicit
  sleep-clears-policy behavior;
- competing YouTube Music／ordinary YouTube isolation, Recovery, lock/unlock and sleep/resume;
- actual sign-out/sign-in startup from the installed stable payload; and
- close-to-tray, restore, explicit Exit, readable settings/state/log JSON and no Error／Critical entry.

The accepted Phase 12B publish profile is unchanged, so do not claim a new startup-performance result unless publish
settings or measurements change. Subjective “no slowdown” is smoke evidence only.

### 6. Clean Windows Sandbox gate

On a fresh supported Windows 11 x64 Sandbox, record Windows edition/build, exact source commit and both artifact hashes,
then verify:

- portable cold start with no separately installed .NET runtime and one process after a second launch;
- per-user install without UAC, Start Menu／Windows Search discovery and one Installed apps entry;
- default-disabled login startup, enable/disable behavior and exact installed-path command;
- Edge `MSEdge` GSMTC discovery, metadata/timeline, Lock session, Play/Pause and one release-only Seek;
- the RC1-to-stable, stable repair, stable-to-RC1 downgrade block and Ready-page cancellation transactions defined
  above;
- real public portable `0.2.0` settings/state compatibility;
- uninstall cleanup, owned versus portable startup boundaries and retained user data; and
- final zero process count, no Tray residue, valid settings/state/log JSON and no Error／Critical entry.

Windows Sandbox ends the environment on sign-out. Record real sign-out/sign-in as unavailable there and close only
that lifecycle row on a persistent supported Windows host using the exact same artifact. Host evidence does not replace
the remaining clean-environment rows, and RC1 evidence does not transfer to stable ProductVersion or digests.

## Integration, stable baseline and publication boundaries

After Phase 14B evidence passes, commit the evidence record without changing the formal artifact source. Push, PR,
merge and task-branch cleanup require explicit authorization. Establish `release/0.3` at the accepted stable source
commit only after confirming that commit and its artifacts passed all gates; retain it as the `0.3.x` hotfix baseline.
Synchronizing `develop` to default branch `main` is part of the stable integration sequence but remains a remote write
covered only by explicit authorization for that sequence.

Tagging and publication are a later, separate approval. If authorized, create a GPG-signed annotated `v0.3.0` tag at
the exact manifest source commit, create a GitHub stable Release, designate it Latest and publicly upload only the ZIP
and Setup listed above. The Release body must include both SHA-256 values, exact source commit, unsigned warning,
supported Windows/RID, Setup-versus-portable guidance, upgrade notes and the Phase 11B limitation.

Do not delete, replace or attach rebuilt assets to `v0.2.0` or `v0.3.0-rc.1`. Keep `release/0.2` and its Worktree during
Phase 14 even after `release/0.3` exists. After the new stable branch and public Release are verified, apply the
[release baseline retention policy](../release-candidate.md#release-baseline-retention-policy); this release's approved
follow-up retired `release/0.2` on 2026-08-26 without changing its historical publication.

