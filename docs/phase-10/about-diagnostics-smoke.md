# Phase 10B About and diagnostics smoke

This protocol validates the in-app support surface before building `0.2.0-rc.3`. Run from a clean Phase 10B commit;
do not treat `0.2.0-rc.2` evidence as transferable.

## Setup

1. Start Media Lock in Release configuration and play one supported media source.
2. Open Settings and scroll to **About and diagnostics**.
3. Repeat the visual checks in Traditional Chinese/Dark and English/Light. The automated palette and localization
   contracts cover the remaining language/theme combinations without duplicating every visual row manually.

## Acceptance

- Version is `0.2.0-rc.3`; Windows shows the expected product, display version, full build and architecture.
- Prerelease/stable and signed/unsigned text matches the executable under test.
- No field, action or focus indicator is clipped. The four action buttons wrap as whole controls when needed.
- Keyboard Tab reaches Copy diagnostics, Open logs, Open support and Report a bug in reading order.
- Copy diagnostics reports environment, routing, catalog, interception, Session count and Recovery facts using normal
  Windows line breaks. It contains no media title, artist, account name, full path, complete settings or target identity.
- The copy confirmation appears in the active language and does not resize the card.
- Open logs opens `%LocalAppData%\MediaLock\logs`; creating the folder when absent is acceptable.
- Open support reaches the repository Issues page; Report a bug reaches the canonical Bug report form.
- Returning to Settings preserves unsaved setting edits. Support actions do not save or close Settings.
- One physical Play/Pause still controls only the routed target; the competing source remains unchanged.
- No crash, unhandled dialog, duplicate process or Error/Critical diagnostic is produced.

Record the exact commit, executable version, Windows build, language/theme combinations and any skipped action.

## Local host acceptance — 2026-08-24

- Source: `codex/feat/phase-10b-about-diagnostics`, Release build immediately preceding review commit `495c139`.
- Traceability: commit `495c139` contains the exact tested executable sources; its later review-only follow-up changes
  the diagnostic interception fact and acceptance documentation, so those changes require automated verification.
- Executable version: `0.2.0-rc.3`.
- Windows: Windows 11 Pro 25H2, build 26200.9168, x64.
- Traditional Chinese/Dark and English/Light layout: pass.
- Copy diagnostics, Open logs, Open support and Report a bug: pass; no form was submitted.
- Unsaved Recovery timeout remained unchanged while support actions ran: pass.
- Keyboard focus order across all four actions: pass.
- Physical Play/Pause routed only to the selected target; competing ordinary YouTube remained unchanged: pass.
- Explicit notification-area Exit: pass.
- Additional errors or crashes: none reported.

Automated clipboard inspection also found 12 CRLF separators, zero lone LF/CR characters and none of the current
media title, artist or user-profile path in the copied summary.
