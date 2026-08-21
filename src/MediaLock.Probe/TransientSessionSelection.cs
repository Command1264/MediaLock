namespace MediaLock.Probe;

public enum TransientSelectionStatus
{
    None,
    Selected,
    Recovering,
    Unavailable,
}

public sealed class TransientSessionSelection<TSession>
    where TSession : class
{
    private readonly Func<TSession, string> sourceSelector;
    private readonly TimeSpan recoveryWindow;
    private string? selectedSource;
    private DateTimeOffset? recoveryStartedAt;

    public TransientSessionSelection(
        Func<TSession, string> sourceSelector,
        TimeSpan recoveryWindow)
    {
        this.sourceSelector = sourceSelector;
        this.recoveryWindow = recoveryWindow;
    }

    public TSession? Current { get; private set; }

    public string? SelectedSource => selectedSource;

    public TransientSelectionStatus Status { get; private set; }

    public void Select(TSession session)
    {
        Current = session;
        selectedSource = sourceSelector(session);
        recoveryStartedAt = null;
        Status = TransientSelectionStatus.Selected;
    }

    public void Clear()
    {
        Current = null;
        selectedSource = null;
        recoveryStartedAt = null;
        Status = TransientSelectionStatus.None;
    }

    public bool Expire(DateTimeOffset observedAt)
    {
        if (Status is TransientSelectionStatus.Recovering &&
            recoveryStartedAt is { } startedAt &&
            observedAt - startedAt >= recoveryWindow)
        {
            Current = null;
            recoveryStartedAt = null;
            Status = TransientSelectionStatus.Unavailable;
            return true;
        }

        return false;
    }

    public void Observe(IReadOnlyList<TSession> sessions, DateTimeOffset observedAt)
    {
        if (Status is TransientSelectionStatus.None)
        {
            return;
        }

        if (Status is TransientSelectionStatus.Unavailable)
        {
            SelectUniqueCandidate(sessions);
            return;
        }

        if (Current is not null && sessions.Any(session => ReferenceEquals(session, Current)))
        {
            return;
        }

        Current = null;
        recoveryStartedAt ??= observedAt;
        Status = TransientSelectionStatus.Recovering;
        if (observedAt - recoveryStartedAt >= recoveryWindow)
        {
            Expire(observedAt);
            SelectUniqueCandidate(sessions);
            return;
        }

        SelectUniqueCandidate(sessions);
    }

    private void SelectUniqueCandidate(IReadOnlyList<TSession> sessions)
    {
        var candidates = sessions
            .Where(session => sourceSelector(session) == selectedSource)
            .Take(2)
            .ToArray();

        if (candidates.Length == 1)
        {
            Select(candidates[0]);
        }
    }
}
