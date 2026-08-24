# Use Inno Setup for the first installable package

Status: Proposed

## Context

Media Lock `0.3.x` needs an ordinary-user installation path that appears in the Start menu and Windows Search,
supports upgrade and uninstall, and preserves the exact executable path used by the existing current-user login-startup
adapter. The public release is currently unsigned, contains one self-contained `win-x64` executable, requires no
service or driver, and stores user data outside the program directory under `%LocalAppData%\MediaLock\`.

The packaging choice must not silently replace the supported portable ZIP or imply that a package format removes
SmartScreen and signing limitations. The detailed comparison and primary-source evidence are recorded in
[Windows installation packaging options](../research/windows-installation-packaging-options.md).

## Decision

Subject to implementation approval, use an Inno Setup per-user EXE installer for the first installable `0.3.x`
package:

- install without elevation to `%LocalAppData%\Programs\MediaLock\`;
- keep one permanent `AppId`, install scope and unversioned install directory across compatible `0.3.x` upgrades;
- create a current-user Start Menu shortcut and an Installed apps uninstall entry;
- package the same reviewed self-contained `MediaLock.exe` used by the portable ZIP;
- leave `Start with Windows` opt-in and owned by Media Lock Settings;
- preserve `%LocalAppData%\MediaLock\` on uninstall by default; and
- continue publishing the portable ZIP until installed upgrade and rollback pass the clean-Windows gate.

The installer and portable archive will have separate digests and provenance records tied to the same source commit.
The installer remains explicitly unsigned until a separate signing decision and must not promise that SmartScreen or
Smart App Control warnings will be absent.

## Consequences

The stable install path keeps an enabled `"<path>" --startup` HKCU Run value valid across an in-place upgrade. Uninstall
may remove that value only when its name and complete value exactly match the installed executable; it must not delete
a value owned by a portable copy.

Inno Setup provides adequate upgrade and uninstall behavior for the present single-file product, but it is not an MSI
transaction. Documentation and tests will state the observed cancellation, failure and downgrade behavior instead of
claiming full transactional rollback or automatic update.

MSIX is deferred while direct public installation would require a trusted signature and a packaged StartupTask plus a
new Windows-integration validation matrix. MSI/WiX is deferred until enterprise deployment, repair, machine-wide
installation or Windows Installer inventory becomes an explicit requirement. Package compression is not accepted as
a substitute for Phase 12B runtime-footprint measurement.
