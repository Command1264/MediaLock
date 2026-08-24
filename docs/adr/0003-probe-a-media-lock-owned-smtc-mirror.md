# Probe a Media Lock-owned SMTC mirror

The documented `GlobalSystemMediaTransportControlsSessionManager` API lets Media Lock request the manager, enumerate
Sessions and observe Windows Current Session. It does not expose an operation that sets Windows Current Session.
Claiming that Media Lock can force the Windows native media surface to select its routed target would therefore exceed
the documented platform contract.

Media Lock will first build a disposable Phase 11B probe that publishes a Media Lock-owned SMTC Media Session through
the documented desktop `ISystemMediaTransportControlsInterop.GetForWindow` boundary. It may mirror the routed target's
metadata, playback state, timeline and capabilities, and translate system-surface events into the existing serialized
Application/Router command path.

The mirror Session must be excluded from Media Lock discovery and routing before any snapshot reaches Core. Its events
must retain capture-time target identity, dispatch once and never feed back into the mirror. Recovery, suspend, target
change and shutdown must clear or disable stale published state.

The probe records whether supported Windows builds actually make the mirror useful on the native media surface. A
created Session or accepted API call is not evidence that Windows selected it. Production Phase 11C proceeds only after
an explicit compatibility-backed decision. If selection is unreliable, Media Lock will document the limitation rather
than use undocumented APIs; any custom on-screen display is a separate product surface.

References:

- [GlobalSystemMediaTransportControlsSessionManager](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionmanager?view=winrt-26100)
- [ISystemMediaTransportControlsInterop::GetForWindow](https://learn.microsoft.com/en-us/windows/win32/api/systemmediatransportcontrolsinterop/nf-systemmediatransportcontrolsinterop-isystemmediatransportcontrolsinterop-getforwindow)
- [Windows Runtime APIs for desktop apps](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/winrt-api-desktop-app-support)
- [Manual control of system media transport controls](https://learn.microsoft.com/en-us/windows/apps/develop/media-playback/system-media-transport-controls)
