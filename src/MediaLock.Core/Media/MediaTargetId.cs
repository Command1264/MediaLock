namespace MediaLock.Core.Media;

public readonly record struct MediaTargetProviderId
{
    private MediaTargetProviderId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static MediaTargetProviderId Gsmtc { get; } = new("gsmtc");

    public static MediaTargetProviderId Browser { get; } = new("browser");

    public static MediaTargetProviderId From(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new MediaTargetProviderId(value);
    }

    public override string ToString() => Value;
}

public readonly record struct MediaTargetId
{
    private MediaTargetId(MediaTargetProviderId provider, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Provider = provider;
        Value = value;
    }

    public MediaTargetProviderId Provider { get; }

    public string Value { get; }

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Provider.Value) &&
        !string.IsNullOrWhiteSpace(Value);

    public static MediaTargetId FromGsmtc(SessionKey session) =>
        new(MediaTargetProviderId.Gsmtc, session.Value);

    public static MediaTargetId FromBrowserPageBinding(string bindingId) =>
        new(MediaTargetProviderId.Browser, bindingId);

    public static MediaTargetId FromProvider(
        MediaTargetProviderId provider,
        string opaqueValue)
    {
        if (string.IsNullOrWhiteSpace(provider.Value))
        {
            throw new ArgumentException("A media target provider is required.", nameof(provider));
        }

        return new MediaTargetId(provider, opaqueValue);
    }

    public static implicit operator MediaTargetId(SessionKey session) => FromGsmtc(session);

    public override string ToString() => $"{Provider}:{Value}";
}
