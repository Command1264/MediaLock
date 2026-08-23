using Windows.Media.Control;

namespace MediaLock.Probe;

internal sealed record PreparedMediaRoute(
    MediaKeyCommand Command,
    GlobalSystemMediaTransportControlsSession Target);
