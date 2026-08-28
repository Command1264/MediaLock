# Media Lock UI Design Language

This document is the source of truth for visible WPF component styling. It keeps new controls consistent with the
existing Media Lock surface without duplicating theme values in individual Views.

## Component contract

- Define reusable control chrome in `Themes/Controls.xaml`; Views select semantic variants and own only layout.
- Resolve every theme-dependent color through a semantic brush from `Light.xaml` or `Dark.xaml`.
- Use an 8 px corner radius for standard interactive controls, 6 px for compact item surfaces, 10–16 px for cards
  and windows, and 5 px for the fixed 18 × 18 CheckBox indicator.
- Standard interactive controls have a minimum height of 36 px. Compact controls retain a usable target without
  changing size when their state changes.
- Every interactive template accounts for normal, pointer-over, pressed or selected, keyboard-focus and disabled
  states where those states apply. Input validation uses `DangerBrush` without changing outer geometry.
- Accent-filled controls use `OnAccentBrush` for foreground content so both palettes retain contrast.
- Focus, selection and asynchronous state changes preserve the control's outer dimensions and surrounding layout.
- Preserve keyboard navigation, access keys, automation names and required WPF template parts when replacing native
  templates.

## Ownership boundaries

WPF owns the client-area controls, Settings chrome and theme palettes. Windows owns the main-window frame and the
notification-area context menu. Map supported Windows-owned surfaces to the resolved theme when an API exists; keep
the native surface when it does not instead of imitating only part of its behavior.

## Current component coverage

The shared dictionary supplies custom templates for Button, Routing Mode ToggleButton, ScrollBar, TextBox,
ProgressBar, Slider, ComboBox, ComboBoxItem, CheckBox and ListBoxItem. Card, status-pill and error surfaces use shared
styles. Plain layout containers and text-only elements consume semantic brushes and typography but require no chrome.

Settings About and diagnostics uses the existing `CardStyle`, secondary text and standard Button templates. Facts use
a compact label/value grid; long Windows descriptions wrap instead of clipping. Actions use a `WrapPanel` so translated
labels retain standard height and move as whole controls on narrower rows. Copy confirmation uses `SuccessBrush`,
adapter failures use the existing Settings `DangerBrush` error surface, and neither state changes control geometry.
Every action has a descriptive automation name; the confirmation is a polite live region.

Human-readable source labels use ordinary text hierarchy rather than new chrome. Main Session rows, the current-target
surface and Settings Priority Rules show one shared friendly presentation. The exact raw identity remains available as
a tooltip and accessibility help text; duplicate friendly labels receive a visible raw-ID qualifier. Friendly-name
fallback or refresh must not resize surrounding controls or change the selected／routed identity.

Main-window media sources use expandable cards inside one vertically bounded outer `ScrollViewer`. The compact
chevron action changes expansion only; the adjacent selection surface changes source scope only. Exact Browser pages
and Windows media Sessions remain visibly labeled child rows with their own identities and capabilities. Nested child
lists do not create independent scrollbars, and expanding a card never moves the fixed transport-control card.
Browser and Windows child rows use the same source, metadata, fixed-width secondary-action and shared-size playback
status columns. Both render the playback label with `MediaPlaybackStatusPillStyle`; a Browser row uses the secondary
action column for a compact overflow menu, while a Windows row leaves that reserved column empty. Page authorization
revocation lives in that row menu and the ordinary Lock Session mode action handles the selected Browser or Windows
child. The overflow trigger keeps its background and border transparent in every state; a theme-specific glyph shadow
makes the action discoverable without a delayed tooltip, hover and press change only glyph opacity, and keyboard focus
uses the accent glyph, so the selected row surface remains uninterrupted. Its
client-area menu owns its complete template instead of inheriting WPF's icon gutter or system-colored
surface; it uses shared theme brushes, 36 px interaction height, focusable menu semantics and localized automation
names. Child-row selection and keyboard focus preserve a 1 px border and alter only semantic colors. Mouse-wheel input
over either nested child list is forwarded to the bounded outer `ScrollViewer`. Playback-status pills share one
adaptive-width column across the complete media-source surface: the longest visible string in the active language sets
the width, every pill stretches to that width, and every status label is centered horizontally and vertically.

The Browser Extension Popup follows Chromium's resolved UI locale rather than owning a separate language selector.
English is the fallback and Traditional Chinese is supplied explicitly. Its two authorization actions form one
vertical action group with a stable 10 px gap so translated labels never collide or rely on incidental wrapping.
Status prose wraps inside the Popup width and uses the same locale as the static labels.

Transient success or informational feedback clears after five seconds and immediately when its owning surface closes.
A newer message replaces the previous message and its pending timeout. Visible localized feedback refreshes when the UI
language changes. Actionable errors are not transient: keep them visible until the user dismisses them or the
underlying operation succeeds.

Before adding another visible control type, first decide whether an existing shared style or semantic variant covers
it. Add a shared template when the platform default would introduce a different radius, palette or interaction
language.

## Verification

For a changed or new control:

1. Add a WPF contract test for stable geometry, required template parts or state semantics when it can be asserted
   without screenshot matching.
2. Build the XAML in Release configuration and run the App test project.
3. Inspect Light and Dark themes in English and Traditional Chinese at the main window's minimum supported size.
4. Exercise pointer, keyboard focus, disabled, selected or validation states that apply and confirm there is no
   clipping, overlap, contrast loss or size shift.

