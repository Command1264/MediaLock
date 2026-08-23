# Phase 7B visual refresh smoke test

Date: 2026-08-23

## Scope

- Production WPF executable from `codex/feat/phase-7b-visual-refresh`
- Windows 11 host with Brave GSMTC media available
- Traditional Chinese Windows UI

## Procedure

1. Start Media Lock with Windows theme and inspect the main window at its default and minimum sizes.
2. Open the fixed frameless Settings surface and inspect every card, ComboBox, Priority Rule row, slim scrollbar,
   focus state, Cancel action and sticky Save action; drag it from the header and close it with Escape.
3. Save Dark, confirm Main and Settings refresh immediately, then route Play/Pause once.
4. Repeat with Light.
5. Repeat language-sensitive layout checks in English and Traditional Chinese.
6. Open Settings and confirm the main window cannot be manipulated. Alt+Tab to another application and back, then
   Cancel; confirm focus returns directly to the main window without an intervening application flash.
7. Confirm the native main title bar follows Light and Dark, then restore the preferred language and theme,
   explicitly Exit and confirm no process remains.

## Results

| Check | Expected | Result |
| --- | --- | --- |
| Shared visual system | Main and Settings use consistent cards, spacing, typography and controls | Pass in initial production-WPF inspection |
| Light client area | Text, cards, selection, disabled controls and focus remain readable | Pass in initial production-WPF inspection |
| Dark client area | Root background, text, cards, ComboBoxes, accent-button text and selection remain readable | Pass in production-WPF inspection and user acceptance |
| Theme persistence | Windows, Light and Dark persist through schema v5 | Automated coverage and Dark production-WPF restart pass |
| Immediate theme switch | Successful Save refreshes existing windows; failed Save does not switch | Automated seam coverage and Light/Dark user acceptance pass |
| English and Traditional Chinese | Both languages remain usable without blocked or clipped controls | English minimum-size Main/Settings and Traditional Chinese user acceptance pass |
| Main minimum size and Settings scrolling | Main remains usable at 720×560; fixed 680×720 Settings remains scrollable | Minimum-size Main and slim Settings scrollbar pass user acceptance |
| Settings window contract | Fixed, non-resizable, frameless and rounded; header drags; Cancel/Escape discard edits | Automated contract/cancel checks and user acceptance pass |
| Modal owner | Main cannot be manipulated while Settings is open | Native `IsWindowEnabled` production-WPF check and user acceptance pass |
| Alt+Tab close return | Closing Settings after application switching returns directly to the main window | One-millisecond foreground sampling shows Settings → Main with no third-party transition; user acceptance reports no flash |
| Native title bar | Main caption follows the resolved Light or Dark theme | DWM attribute reports Dark=1 and Light=0; both appearances and user acceptance pass |
| Appearance selection stability | Opening Settings and rebuilding localized options preserve non-empty language/theme selections | WPF regression test was RED before the fix and passes 10/10 after binding complete option objects; user acceptance passed ten opens plus English/Light save and reopen |
| Routing regression | Play/Pause changes the intended media exactly once | User acceptance passes; ordinary YouTube remains unchanged |
| Lifecycle | Explicit Exit leaves no Media Lock process | User completed notification-area Exit; process count verified as zero |

The visual inspection found and corrected Dark-theme root-background, ComboBox and accent-button text inheritance
issues before manual acceptance. Views bind their root foreground/background directly to dynamic palette resources;
ComboBox and primary buttons use theme-aware templates. User acceptance also confirmed stable routing-action spacing,
no focus-induced layout shift, readable Dark Main/Settings/dropdowns and the slim functional scrollbar. User
acceptance also passed the fixed frameless window, rounded corners, header drag, Cancel/Escape restore and the original
Alt+Tab foreground return. The later modal owner, direct foreground return and Light/Dark native-title-bar changes also
pass focused user acceptance without routing regression. The preferred language and theme must be restored at the end
of the smoke test. The user later found that language or theme could open with an empty selection. A deterministic
WPF regression reproduced the issue: string `SelectedValue` binding lost its match while the localized option source
was initialized or replaced. Binding the complete localized option now preserves the durable value and reselects the
matching replacement item. Focused user acceptance passed ten consecutive opens without an empty value, then saved
and reopened English/Light successfully before restoring the preferred settings.
