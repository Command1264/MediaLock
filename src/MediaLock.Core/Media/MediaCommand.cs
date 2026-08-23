namespace MediaLock.Core.Media;

public enum MediaCommandKind
{
    Unknown,
    Play,
    Pause,
    TogglePlayPause,
    Previous,
    Next,
    Stop,
    SeekAbsolute,
}

public readonly record struct MediaCommand
{
    private MediaCommand(MediaCommandKind kind, TimeSpan? absolutePosition = null)
    {
        Kind = kind;
        AbsolutePosition = absolutePosition;
    }

    public MediaCommandKind Kind { get; }

    public TimeSpan? AbsolutePosition { get; }

    public static MediaCommand Play { get; } = new(MediaCommandKind.Play);

    public static MediaCommand Pause { get; } = new(MediaCommandKind.Pause);

    public static MediaCommand TogglePlayPause { get; } = new(MediaCommandKind.TogglePlayPause);

    public static MediaCommand Previous { get; } = new(MediaCommandKind.Previous);

    public static MediaCommand Next { get; } = new(MediaCommandKind.Next);

    public static MediaCommand Stop { get; } = new(MediaCommandKind.Stop);

    public static MediaCommand SeekAbsolute(TimeSpan position)
    {
        if (position < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(position),
                position,
                "An absolute Seek position cannot be negative.");
        }

        return new MediaCommand(MediaCommandKind.SeekAbsolute, position);
    }

    public override string ToString() => Kind.ToString();
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
    SeekAbsolute = 1 << 6,
    All = Play | Pause | TogglePlayPause | Previous | Next | Stop | SeekAbsolute,
}

public static class MediaCommandCapabilitiesExtensions
{
    public static bool Supports(this MediaCommandCapabilities capabilities, MediaCommand command) =>
        (capabilities & command.ToCapability()) != 0;

    private static MediaCommandCapabilities ToCapability(this MediaCommand command) => command.Kind switch
    {
        MediaCommandKind.Play => MediaCommandCapabilities.Play,
        MediaCommandKind.Pause => MediaCommandCapabilities.Pause,
        MediaCommandKind.TogglePlayPause => MediaCommandCapabilities.TogglePlayPause,
        MediaCommandKind.Previous => MediaCommandCapabilities.Previous,
        MediaCommandKind.Next => MediaCommandCapabilities.Next,
        MediaCommandKind.Stop => MediaCommandCapabilities.Stop,
        MediaCommandKind.SeekAbsolute => MediaCommandCapabilities.SeekAbsolute,
        _ => MediaCommandCapabilities.None,
    };
}
