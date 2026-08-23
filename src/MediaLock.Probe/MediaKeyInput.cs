namespace MediaLock.Probe;

internal sealed record MediaKeyInput(
    MediaKeyCommand Command,
    PreparedMediaRoute? Route,
    string Reason);
