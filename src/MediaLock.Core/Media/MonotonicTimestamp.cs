namespace MediaLock.Core.Media;

public readonly record struct MonotonicTimestamp(long Value, long Frequency)
{
    public bool TryGetElapsed(
        long currentValue,
        long currentFrequency,
        out TimeSpan elapsed)
    {
        elapsed = TimeSpan.Zero;
        if (Frequency <= 0 ||
            currentFrequency != Frequency ||
            currentValue < Value)
        {
            return false;
        }

        var elapsedSeconds = (double)(((decimal)currentValue - Value) / Frequency);
        if (!double.IsFinite(elapsedSeconds) ||
            elapsedSeconds < 0d ||
            elapsedSeconds > TimeSpan.MaxValue.TotalSeconds)
        {
            return false;
        }

        elapsed = TimeSpan.FromSeconds(elapsedSeconds);
        return true;
    }
}
