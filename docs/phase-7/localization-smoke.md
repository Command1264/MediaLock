# Phase 7A localization smoke test

Date: 2026-08-23

## Environment

- Windows 11 Pro, Traditional Chinese UI
- Phase 7A dirty-worktree `win-x64` self-contained single-file test publish
- Existing schema-v3 user settings loaded through the production JSON repository

## Procedure

1. Start the test executable with the language preference set to Windows language.
2. Inspect the main window, Session list, playback status and media controls.
3. Open Settings and inspect headings, descriptions, choices and accessibility names.
4. Inspect the notification-area commands and status.
5. Select English, save, confirm the current process refreshes immediately, then explicitly exit, restart and repeat
   steps 2-4.
6. Select Traditional Chinese, save, confirm immediate refresh, then explicitly exit, restart and repeat steps 2-4.
7. Restore the original language preference.

## Results

| Check | Expected | Actual |
| --- | --- | --- |
| Windows-language resolution | Traditional-Chinese Windows selects `zh-TW` | Pass |
| Main window | Commands, routing labels, playback state and accessibility names use Traditional Chinese | Pass |
| Settings | General, language, Recovery and Priority Rules surfaces use Traditional Chinese without clipping that blocks use | Pass |
| Resource completeness | English and Traditional Chinese contain the same keys; every source reference resolves | Pass; 101 keys in each resource and no missing source key |
| Single-file publish | Localization does not add a second packaged file | Pass; publish output contained only `MediaLock.exe` |
| English restart | Saved `en-US` restarts with English main, Settings and notification-area surfaces | Pass; main, Settings and Tray were English |
| Traditional-Chinese explicit restart | Saved `zh-TW` restarts with Traditional Chinese surfaces | Pass; main, Settings and Tray were Traditional Chinese |
| Routing/lifecycle regression | Language changes do not alter routing or leave a process after explicit Exit | Pass for restart baseline; Play/Pause changed media once in both languages and each explicit Exit left zero processes |
| Immediate English application | Saving `en-US` updates existing main, Settings and Tray surfaces without restart | Pass |
| Immediate Traditional-Chinese application | Saving `zh-TW` updates existing surfaces without restart | Pass |
| Language-native choices | Language list always exposes `English` and `繁體中文` | Pass |

The immediate-switch smoke test also confirmed that main, Settings and Tray text refreshed together, Play/Pause
changed media exactly once and no error or crash occurred. Testing restored the original Windows-language preference.
Automated tests cover culture resolution, English and Traditional-Chinese resource lookup, schema-v3 migration,
language choice projection and persistence through the application intent seam.
