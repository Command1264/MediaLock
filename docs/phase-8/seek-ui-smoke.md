# Phase 8B routed Seek and interactive timeline

Date: 2026-08-23

## Scope

- Production absolute Seek through the existing Media Command routing path.
- Interactive routed-target timeline for mouse, touch and keyboard.
- No Seek hotkey, setting, persistence or new dependency.

## Application matrix

| Source | State | Interaction | API result | Timeline confirmation | Competing source unchanged |
| --- | --- | --- | --- | --- | --- |
| Brave YouTube Music PWA | Playing | 25% and 75% pointer gestures | Accepted | Position matched | Pass: ordinary YouTube unchanged |
| Brave YouTube Music PWA | Paused | Pointer and keyboard | Accepted | Position matched | Pass: ordinary YouTube unchanged |
| Brave ordinary YouTube | Playing | 25% and 75% pointer gestures | Accepted | Position matched | Pass: YouTube Music unchanged |
| Brave ordinary YouTube | Paused | Pointer Seek | Accepted | Position matched | Pass: YouTube Music unchanged |

## Lifecycle and presentation matrix

| Scenario | Expected | Result |
| --- | --- | --- |
| One drag or key-hold | Exactly one routed Seek | Pass |
| Direct track click | Exactly one routed Seek at the clicked position | Pass |
| Press an empty track point, drag, then release outside the track | Preview follows the pointer; release submits exactly one final Seek | Pass; target did not move before release and competing source remained unchanged |
| One touch drag (when a touch device is available) | Exactly one routed Seek despite WPF touch-to-mouse promotion | Not run: no touch device |
| Unsupported or invalid timeline | Timeline remains visible but non-interactive | Pass in deterministic ViewModel and Router coverage |
| Session recreation during preview or confirmation | Preview is cancelled; recovered target uses its own timeline | Pass in deterministic ViewModel coverage |
| Locked-target list selection across reload or track change | No unrelated fallback row is selected; recovered target is selected unless the user explicitly chose another row | Pass in App Lock and Session Lock |
| Mode-independent list selection bookmark | A selection that disappears remains blank and returns within the Recovery timeout in all four modes; manual replacement wins, timeout or ambiguity never selects the first row | Pass in all four Routing Modes; Routing Mode remained unchanged |
| Rejected, failed or confirmation timeout | Observed position returns with an actionable error | Pass in deterministic ViewModel coverage |
| Error dismissal | The explicit close control removes the message; blank Session-list space has no effect | Pass in ViewModel/WPF coverage; blank-list behavior also passed manually |
| English and Traditional Chinese | Accessible text and time presentation remain correct | Pass |
| Light, Dark and minimum window size | Slider and transport controls remain legible, usable and unclipped | Pass; final Stop-control edge rechecked after safe-inset fix |
| Four Routing Mode controls | Exactly one mode has a stable accent/check treatment, including Recovery | Pass in Light and Dark; no size shift |
| Physical Play/Pause | One route to the selected target; competing source unchanged | Pass through Phase 8C production Hook; controlled normal, long-press and rapid-input groups routed without duplicates |
