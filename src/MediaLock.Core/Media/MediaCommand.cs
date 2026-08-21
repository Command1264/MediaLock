namespace MediaLock.Core.Media;

public enum MediaCommand
{
    Play,
    Pause,
    TogglePlayPause,
    Previous,
    Next,
    Stop,
}

[Flags]
public enum MediaCommandCapabilities
{
    None = 0,
    Play = 1 << 0,
    Pause = 1 << 1,
    TogglePlayPause = 1 << 2,
    Previous = 1 << 3,
    Next = 1 << 4,
    Stop = 1 << 5,
    All = Play | Pause | TogglePlayPause | Previous | Next | Stop,
}

public static class MediaCommandCapabilitiesExtensions
{
    public static bool Supports(this MediaCommandCapabilities capabilities, MediaCommand command) =>
        (capabilities & command.ToCapability()) != 0;

    private static MediaCommandCapabilities ToCapability(this MediaCommand command) => command switch
    {
        MediaCommand.Play => MediaCommandCapabilities.Play,
        MediaCommand.Pause => MediaCommandCapabilities.Pause,
        MediaCommand.TogglePlayPause => MediaCommandCapabilities.TogglePlayPause,
        MediaCommand.Previous => MediaCommandCapabilities.Previous,
        MediaCommand.Next => MediaCommandCapabilities.Next,
        MediaCommand.Stop => MediaCommandCapabilities.Stop,
        _ => MediaCommandCapabilities.None,
    };
}
