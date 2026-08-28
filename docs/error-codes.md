# Media Lock error codes

Media Lock displays a stable code beside every known user-facing warning or error. Include the exact code in a bug or
compatibility report. The visible message follows the active English or Traditional Chinese UI language; structured
logs and the privacy-safe diagnostic summary use the same latest reported code.

## Compatibility policy

- A published code is never reassigned to a different semantic condition.
- Wording and recovery guidance may improve without changing the code.
- Splitting one condition into independently actionable conditions creates new codes; old codes remain documented.
- Missing locale text falls back to English while preserving the original code.
- Unknown desktop failures use `ML-APP-000`; unknown Browser Integration failures use `ML-BR-000`.
- Codes identify conditions, not individual occurrences. Media Lock separately tracks each occurrence for UI dismissal.

## Desktop application

| Code | Condition | Recommended action |
| --- | --- | --- |
| `ML-APP-000` | Unexpected application error | Retry, then report the code if it continues. |
| `ML-APP-001` | Application startup failed | Restart Media Lock and report the code if it continues. |
| `ML-APP-002` | Clean shutdown failed | Close any remaining Media Lock process before restarting. |
| `ML-APP-003` | An application action could not complete | Retry the action. |
| `ML-CFG-001` | Settings could not be read | Review Settings before saving the safe defaults. |
| `ML-CFG-002` | Runtime state could not be read | Recreate the desired lock from Windows Auto. |
| `ML-CFG-003` | Saved Session Lock target is invalid | Select and lock the Session again. |
| `ML-CFG-004` | Saved App Lock target is invalid | Select and lock the application again. |
| `ML-CFG-005` | Session Lock persistence is unavailable | Continue with Windows Auto or restore persistence. |
| `ML-CFG-006` | App Lock persistence is unavailable | Continue with Windows Auto or restore persistence. |
| `ML-CFG-007` | Runtime-state persistence is unavailable | Retry before changing the startup routing mode. |
| `ML-CFG-008` | Startup routing choice could not be saved | Review Settings and retry. |
| `ML-CFG-009` | Runtime state could not be saved | Retry before restarting Media Lock. |
| `ML-OS-001` | Start-with-Windows monitoring is unavailable | Review the startup setting before relying on it. |
| `ML-CAT-001` | Media catalog stopped | Resume Windows or restart Media Lock. |
| `ML-CAT-002` | Media catalog is unavailable | Resume Windows or restart Media Lock. |
| `ML-CAT-003` | Catalog failure state could not be applied | Restart Media Lock. |
| `ML-CMD-001` | Media command failed | Check the locked target and retry. |
| `ML-CMD-002` | Media target rejected the command | Operate the target directly, then retry. |
| `ML-CMD-003` | Command outcome is unknown | Inspect the target before sending another command. |
| `ML-CMD-004` | Command is unsupported | Use a command advertised by the selected target. |
| `ML-CMD-005` | Command target is unavailable | Select or restore the target. |
| `ML-CMD-006` | Seekable timeline is unavailable | Use the media page or application timeline. |
| `ML-CMD-007` | Seek position is outside the timeline | Choose a position inside the displayed duration. |
| `ML-CMD-008` | Seek result was not confirmed | Check the media page or application. |
| `ML-CMD-009` | Seek was interrupted by target loss or change | Restore the target and retry. |
| `ML-SET-001` | Settings could not be saved | Review the values and retry. |
| `ML-SET-002` | Language or theme could not be applied | Retry; the previous presentation is restored. |
| `ML-SET-003` | Support action failed | Retry or open the destination manually. |
| `ML-SET-004` | Recovery timeout is invalid | Enter a finite value from 0 through 300 seconds. |
| `ML-SET-005` | Repeated-pause window is invalid | Enter a whole number from 1 through 60 seconds. |
| `ML-SET-006` | Repeated-pause count is invalid | Enter a whole number from 2 through 10. |
| `ML-UI-001` | Notification sound failed | Continue using media control without the sound. |
| `ML-DIAG-001` | Diagnostic logging is unavailable | Continue media control and report the code. |
| `ML-INPUT-001` | Global media-key interception could not start | Use in-app controls or let Windows handle the keys. |
| `ML-INPUT-002` | Global media-key interception stopped | Use in-app controls or let Windows handle the keys. |
| `ML-PLAY-001` | Keep Playing could not confirm playback | Turn it off or play the target before enabling it again. |

## Browser Integration

| Code | Condition | Recommended action |
| --- | --- | --- |
| `ML-BR-000` | Unexpected Browser Integration error | Retry, then reload the Extension and page. |
| `ML-BR-001` | Page is not eligible | Open an HTTPS page with one media element. |
| `ML-BR-002` | Extension could not check the page | Reload the Extension and page. |
| `ML-BR-003` | Selected media page is unavailable | Reload the page. |
| `ML-BR-004` | Site permission was denied | Review the browser site permission. |
| `ML-BR-005` | Native Host or Media Lock is unavailable | Start Media Lock and retry. |
| `ML-BR-006` | More than one media element is available | Leave one unambiguous media element. |
| `ML-BR-007` | No controllable media element is available | Start or reveal the page media element. |
| `ML-BR-008` | Document changed during authorization | Wait for loading to finish and retry. |
| `ML-BR-009` | Authorization request is no longer valid | Reopen the Popup and retry. |
| `ML-BR-010` | Page rejected playback | Start playback on the page and retry. |
| `ML-BR-011` | Seek position is outside the page timeline | Choose a position inside the duration. |
| `ML-BR-012` | Desktop Browser access revocation failed | Reload the Extension and retry. |

Codes contain no media title, artist, account name, complete target identity, path or secret. Before sharing logs,
still review the selected excerpt as described in [support guidance](../SUPPORT.md#診斷資料與隱私).
