using System.Globalization;

namespace MediaLock.Probe;

internal readonly record struct SeekProbeRequest(TimeSpan Position)
{
    public long RequestedTicks => Position.Ticks;

    public static bool TryParse(
        string[] parts,
        out SeekProbeRequest request,
        out string? error)
    {
        request = default;
        if (parts.Length != 2)
        {
            error = "Usage: seek <seconds>";
            return false;
        }

        if (!double.TryParse(
                parts[1],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var seconds) ||
            !double.IsFinite(seconds) ||
            seconds < 0)
        {
            error = "Seek seconds must be a finite, non-negative number using '.' as the decimal separator.";
            return false;
        }

        try
        {
            request = new SeekProbeRequest(TimeSpan.FromSeconds(seconds));
            error = null;
            return true;
        }
        catch (OverflowException)
        {
            error = "Seek seconds are outside the supported time range.";
            return false;
        }
    }

    public bool TryValidateTimeline(TimeSpan start, TimeSpan end, out string? error)
    {
        if (end <= start)
        {
            error = $"Selected session has an invalid timeline ({start:c} to {end:c}).";
            return false;
        }

        if (Position < start || Position > end)
        {
            error = $"Seek position {Position:c} must be between {start:c} and {end:c}.";
            return false;
        }

        error = null;
        return true;
    }
}
