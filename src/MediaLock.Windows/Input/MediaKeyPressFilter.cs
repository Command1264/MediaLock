using MediaLock.Core.Input;
using MediaLock.Core.Media;

namespace MediaLock.Windows.Input;

internal static class MediaKeyVirtualKeys
{
    public const uint Next = 0xB0;
    public const uint Previous = 0xB1;
    public const uint Stop = 0xB2;
    public const uint PlayPause = 0xB3;
}

internal enum MediaKeyTransition
{
    KeyDown,
    KeyUp,
}

internal sealed class MediaKeyPressFilter
{
    private readonly Dictionary<uint, bool> pressDecisions = [];

    public bool Process(
        uint virtualKeyCode,
        MediaKeyTransition transition,
        MediaInputHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!TryMap(virtualKeyCode, out var command))
        {
            return false;
        }

        if (transition == MediaKeyTransition.KeyDown)
        {
            if (!pressDecisions.TryGetValue(virtualKeyCode, out var consumed))
            {
                consumed = handler(command);
                pressDecisions.Add(virtualKeyCode, consumed);
            }

            return consumed;
        }

        return pressDecisions.Remove(virtualKeyCode, out var consumedOnKeyDown) &&
            consumedOnKeyDown;
    }

    public void Clear() => pressDecisions.Clear();

    private static bool TryMap(uint virtualKeyCode, out MediaCommand command)
    {
        command = virtualKeyCode switch
        {
            MediaKeyVirtualKeys.PlayPause => MediaCommand.TogglePlayPause,
            MediaKeyVirtualKeys.Next => MediaCommand.Next,
            MediaKeyVirtualKeys.Previous => MediaCommand.Previous,
            MediaKeyVirtualKeys.Stop => MediaCommand.Stop,
            _ => default,
        };

        return virtualKeyCode is
            MediaKeyVirtualKeys.PlayPause or
            MediaKeyVirtualKeys.Next or
            MediaKeyVirtualKeys.Previous or
            MediaKeyVirtualKeys.Stop;
    }
}
