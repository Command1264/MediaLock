# Phase 19 installed Browser Integration lifecycle

Issue: [#74](https://github.com/Command1264/MediaLock/issues/74)

Status: planned; no installed package, registration or release behavior has changed yet.

## Outcome

Make the installed Media Lock package own the complete current-user Browser Integration lifecycle. A person installs
and starts Media Lock normally; Chromium starts the bundled Native Messaging Host only when the separately enabled
Extension connects. The person never builds, registers or separately launches the Native Host.

This phase promotes packaging and ownership, not new routing semantics. Exact Browser Session Lock remains the only
direct Browser Routing Mode, and the complete GSMTC composition remains available when the Extension is absent.

## Entry evidence

Phase 16C already supplies a provider-neutral media-target seam, one production Browser Adapter Module, a fixed-ID
unpacked Chromium Extension and a current-user Native Messaging／named-pipe bridge. Its candidate registration script
proves Chrome and Brave share one Chrome-compatible registry seam and that exact owned registration can be removed
without deleting a foreign value. Phase 17 supplies localized structured problems, and Phase 18 supplies rate-aware
timeline presentation.

That evidence does not qualify an installed package. The current script publishes a framework-dependent Host beneath
a repository artifact directory, writes a development manifest and requires a developer to register it. The stable
installer still contains only `MediaLock.exe`; repair, upgrade and uninstall do not own Browser Integration.

## User contract

- The installed payload contains Media Lock, a self-contained Browser Host and the fixed-ID Extension files in stable
  package-owned locations.
- The Extension remains an explicit browser installation／enablement choice. Media Lock does not silently install it,
  enable Developer mode, broaden site permissions or modify browser policy.
- The Native Host is not a second user application. Chromium launches it on demand, it relays one bounded connection
  to the running Media Lock process and exits when that connection closes.
- Installing, repairing or upgrading Media Lock reconciles one exact current-user Native Messaging registration.
- Uninstall removes only the registration and manifest owned by that exact installed package. User settings, logs,
  browser permissions and unrelated registrations are preserved.
- A portable copy does not claim installed-package ownership. Without a valid installed owner it stays GSMTC-only
  unless the existing development registration workflow was explicitly invoked.
- With no Extension installed or enabled, Media Lock starts without a blocking prompt and retains the complete GSMTC
  feature set.
- Registration failure never reroutes a Browser lock to another page or GSMTC target. It produces a localized,
  actionable, stable `ML-BR-*` problem while ordinary GSMTC routing remains available.

## Installed layout and identity

The implementation must define one versioned installed Browser Integration layout beneath the exact Media Lock
installation root. At minimum it contains:

- one self-contained `win-x64` `MediaLock.BrowserHost.exe` that starts on a supported clean Windows system without a
  separately installed .NET runtime;
- the Host's bounded fixed configuration, either embedded or in one package-owned validated file;
- one generated Native Messaging manifest whose absolute Host path is under the same installed root; and
- the exact Extension files corresponding to the pinned production Extension ID, stored in a stable directory that
  can be selected manually in Chromium.

The main executable, Host, Extension and manifest must carry one build identity in artifact provenance. A package may
not mix files from different commits or versions. The existing portable single-file lane stays independent and does
not acquire an implicit Native Messaging registration.

## Deep installation Module

Put installed ownership behind one small composition-time Interface. Callers provide an immutable installed-package
descriptor and one intent: ensure the exact registration or remove the exact owned registration. The Module returns
one structured result describing the observed ownership state and action; callers do not manipulate registry values,
manifest JSON or Host paths themselves.

The implementation hides:

- installed-versus-portable qualification;
- canonical path and package-root validation;
- manifest construction and atomic replacement;
- current-user Chrome-compatible registry access;
- exact owner comparison and conflict classification;
- repair, same-version idempotence and upgrade replacement;
- cleanup ordering and bounded diagnostics; and
- translation from internal failure categories to structured `ML-BR-*` problems.

The Interface is the test surface. Production uses the Windows registry and filesystem adapters; deterministic tests
use isolated registry and filesystem adapters through internal seams. Core, Router, Browser command dispatch and WPF
must not learn registry paths or installer rules.

## Ownership state machine

The Module resolves one of these observable states before taking an action:

1. **Absent** — no value exists. Ensure writes the package manifest atomically, then registers its exact path.
2. **Owned and current** — the value, manifest, Host path, Extension ID and package identity all match. Ensure is an
   idempotent no-op.
3. **Owned but stale** — the value is owned by a recognized earlier installed Media Lock package. Ensure replaces it
   only after the new installed payload is complete; removal of the predecessor happens after the successor is ready.
4. **Owned but damaged** — the package-owned value or manifest is incomplete or inconsistent. Ensure repairs only
   paths inside the exact current installation and reports the repair result.
5. **Foreign／development／portable** — ownership cannot be proved. Ensure preserves it and returns an actionable
   conflict; Remove Owned also preserves it. Repository artifact paths are not silently promoted into package
   ownership.

Same-user registry access is not treated as a security boundary against the account owner. Exact ownership checks
still prevent ordinary install, repair and uninstall from damaging another Media Lock copy or unrelated Host.

## Lifecycle integration

### Install and repair

The installer places every version-matched file before invoking one non-UI composition command. That command runs
before single-instance, tray, GSMTC and Browser Adapter startup. It qualifies the executing path as the installed
package, applies Ensure once and returns a stable process result for installer evidence.

A normal installed startup may inspect and repair its exact owned registration after interrupted installation or
external deletion. Inspection must be bounded and must not launch a browser, request site permission or block GSMTC
startup. Portable startup never performs this repair.

### Upgrade

Upgrade keeps the established installation root and Native Host name. The new Host and configuration land before the
manifest／registry switch. A browser process already using the previous Host may finish that connection; no installer
step kills a foreign or active Host by filename alone. The next connection launches the new exact Host. Downgrade
protection remains governed by the existing installer version policy.

### Uninstall

The pre-deletion cleanup command combines existing login-startup cleanup with exact Browser Integration cleanup. It
removes the current-user registry value only when it still names the exact package-owned manifest, then removes only
that owned manifest. Extension files under the application root are removed with program files; Chromium Extension
state and granted site permissions are browser-owned and are not silently modified.

If ownership cannot be proved, uninstall preserves the registry value and records an actionable cleanup result rather
than deleting by Host name. User data beneath `%LocalAppData%\MediaLock` remains governed by the existing retention
contract.

## Packaging contract

Phase 19 changes the installer payload contract and therefore requires a new artifact schema or an explicit compatible
schema extension. Provenance must name and hash every installed Browser Integration file. The portable ZIP may remain
the existing one-EXE GSMTC artifact; its manifest must state that installed Browser Integration is absent. The Setup
artifact must state that the Host and unpacked Extension files are included but that browser enablement is manual.

No Phase 19 plan or implementation PR may overwrite published `0.3.0` artifacts. A future version, tag and Release
remain separately authorized work after exact-candidate acceptance.

## Security and privacy invariants

- Registration stays under the current user's Chrome-compatible Native Messaging key and requires no elevation.
- The fixed Host name, production Extension ID, `stdio` type, pipe name and 64 KiB protocol bound remain unchanged
  unless a separately reviewed protocol／distribution decision replaces them.
- The generated manifest permits only the pinned Extension origin and points only to the canonical package Host.
- The Host exposes no TCP／HTTP listener and writes no protocol data to standard output outside Native Messaging.
- Paths received from registry, installer arguments or manifests are untrusted and canonicalized before comparison.
- No operation follows an unvalidated path outside the exact installation or owned manifest location.
- Logs and diagnostic summaries exclude page URL, media metadata, complete Page Binding identity and Native Messaging
  payloads. They may include the stable problem code, ownership state and bounded exception category.

## Automated acceptance

Start RED at the installation Module Interface, then integrate the installer and artifact pipeline.

1. **State matrix:** Absent, current, stale, damaged and foreign states produce deterministic Ensure／Remove results.
2. **Ownership:** foreign, development and portable registrations are never overwritten or deleted; current and
   recognized predecessor installed registrations are repaired or upgraded exactly once.
3. **Path safety:** relative, escaped, differently cased, malformed, missing and outside-root paths fail closed without
   touching their targets.
4. **Atomicity:** a manifest write or registry failure retains the last valid registration or reports Absent／Damaged;
   it never publishes a partial successor.
5. **Idempotence:** repeated install, startup repair, same-version repair and uninstall cleanup produce no duplicate
   keys, manifests or processes.
6. **Composition:** install／cleanup commands run without WPF, tray, GSMTC, input interception or single-instance
   activation. Ordinary installed and portable startup follow their distinct ownership policies.
7. **Packaging:** source commit, main EXE, Host, configuration and Extension hashes are one artifact identity. A clean
   Windows test launches the Host without a machine-wide .NET runtime.
8. **Transactions:** fresh install, same-version repair, upgrade, rejected downgrade, cancellation and uninstall
   preserve settings, startup ownership and foreign Browser registrations according to contract.
9. **Compatibility lanes:** no Extension installed retains the complete GSMTC suite without a blocking prompt;
   Extension enabled discovers one target and routes Play, Pause, Toggle and Seek through the installed Host.
10. **Failure presentation:** every visible install, repair or ownership conflict uses localized English／Traditional
    Chinese copy, one stable `ML-BR-*` code and privacy-safe diagnostics.

The complete .NET and Extension suites, dual PowerShell registration contract, formatting, Release build and artifact
inspection remain mandatory. Packaging tests replace development-script evidence; they do not infer installed
qualification from the candidate registry test.

## Manual acceptance matrix

Use the exact Setup candidate on a supported clean Windows x64 environment and record hashes, Windows／browser version,
Extension identity and actual result.

1. Fresh per-user install with no Extension: one app instance／tray icon, full GSMTC operation, no elevation and no
   blocking Browser prompt.
2. Manually load the bundled Extension directory in Chrome and Brave: the browser launches the bundled Host without
   a separate command, one authorized page appears and exact commands affect no competitor.
3. Exit and restart Media Lock while the trusted page remains: the Extension reconnects to the installed Host and
   republishes exactly one target without page reload.
4. Same-version repair and forward upgrade: registration moves only after the new payload is ready; no ghost target,
   duplicate Host or stale path remains.
5. Foreign／development registration conflict: installation preserves it, GSMTC remains usable and the exact localized
   problem explains the safe remediation.
6. Uninstall with an owned registration: program and Browser Integration files plus the exact registry value disappear;
   user data and browser permission state follow their documented owners.
7. Uninstall with a replaced foreign registration: the foreign value remains byte-for-byte unchanged.
8. Reinstall after clean uninstall: registration and direct Session Lock work without repository files, Codex or a
   separately started Native Host.

## Documentation and delivery sequence

1. Accept this plan and add or supersede an ADR for installed package ownership before implementation.
2. Implement the deep installation Module and deterministic state matrix.
3. Extend publish／installer transaction contracts and package the Host／Extension files.
4. Integrate non-UI install, repair and uninstall commands plus localized problems.
5. Run the full automated gate and produce an exact ignored candidate.
6. Complete the eight-row manual matrix before any merge into `develop`.
7. Treat tag, release publication, Extension-store distribution and release-baseline changes as separate approvals.

## Explicit non-goals

- Chrome Web Store, Edge Add-ons or another Extension-store submission／publication.
- Silent Extension installation, enterprise browser policy or forced Developer mode.
- Browser App Lock, Priority Rules, Windows Auto or persisted Browser lock migration.
- Firefox, nested-frame selection, DRM／Canvas／private-player control or new Generic Adapter commands.
- Suppressing browser GSMTC publication or making Media Lock the Windows Current Session.
- Tagging, releasing, publishing artifacts or retiring `release/0.3`.
