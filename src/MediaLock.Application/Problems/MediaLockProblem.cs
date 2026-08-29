using System.Collections.Immutable;

namespace MediaLock.Application;

public enum MediaLockProblemSeverity
{
    Warning,
    Error,
}

public enum MediaLockProblemId
{
    Unknown,
    StartupFailed,
    ShutdownFailed,
    ApplicationOperationFailed,
    SettingsLoadFailed,
    RuntimeStateLoadFailed,
    DefaultSessionLockTargetInvalid,
    DefaultAppLockTargetInvalid,
    SessionLockPersistenceUnavailable,
    AppLockPersistenceUnavailable,
    RuntimeStatePersistenceUnavailable,
    StartupRoutingModeSaveFailed,
    RuntimeStateSaveFailed,
    LoginStartupMonitoringUnavailable,
    CatalogStopped,
    CatalogUnavailable,
    CatalogTransitionFailed,
    CommandFailed,
    CommandRejected,
    CommandOutcomeUnknown,
    CommandUnsupported,
    CommandTargetUnavailable,
    SeekTimelineUnavailable,
    SeekOutOfRange,
    SeekNotConfirmed,
    SeekInterrupted,
    SettingsSaveFailed,
    SettingsPresentationApplyFailed,
    SupportActionFailed,
    RecoveryTimeoutInvalid,
    RepeatedPauseWindowInvalid,
    RepeatedPauseCountInvalid,
    NotificationSoundFailed,
    DiagnosticLoggingUnavailable,
    MediaInputStartupFailed,
    MediaInputStopped,
    TargetAuthorizationRevokeFailed,
    PlaybackCorrectionFailed,
}

public sealed record MediaLockProblemDefinition(
    MediaLockProblemId Id,
    string Code,
    MediaLockProblemSeverity DefaultSeverity);

public static class MediaLockProblemCatalog
{
    public static ImmutableArray<MediaLockProblemDefinition> Definitions { get; } =
    [
        Define(MediaLockProblemId.Unknown, "ML-APP-000", MediaLockProblemSeverity.Error),
        Define(MediaLockProblemId.StartupFailed, "ML-APP-001", MediaLockProblemSeverity.Error),
        Define(MediaLockProblemId.ShutdownFailed, "ML-APP-002", MediaLockProblemSeverity.Error),
        Define(MediaLockProblemId.ApplicationOperationFailed, "ML-APP-003", MediaLockProblemSeverity.Error),
        Define(MediaLockProblemId.SettingsLoadFailed, "ML-CFG-001", MediaLockProblemSeverity.Warning),
        Define(MediaLockProblemId.RuntimeStateLoadFailed, "ML-CFG-002", MediaLockProblemSeverity.Warning),
        Define(MediaLockProblemId.DefaultSessionLockTargetInvalid, "ML-CFG-003", MediaLockProblemSeverity.Warning),
        Define(MediaLockProblemId.DefaultAppLockTargetInvalid, "ML-CFG-004", MediaLockProblemSeverity.Warning),
        Define(MediaLockProblemId.SessionLockPersistenceUnavailable, "ML-CFG-005", MediaLockProblemSeverity.Warning),
        Define(MediaLockProblemId.AppLockPersistenceUnavailable, "ML-CFG-006", MediaLockProblemSeverity.Warning),
        Define(MediaLockProblemId.RuntimeStatePersistenceUnavailable, "ML-CFG-007", MediaLockProblemSeverity.Error),
        Define(MediaLockProblemId.StartupRoutingModeSaveFailed, "ML-CFG-008", MediaLockProblemSeverity.Error),
        Define(MediaLockProblemId.RuntimeStateSaveFailed, "ML-CFG-009", MediaLockProblemSeverity.Error),
        Define(MediaLockProblemId.LoginStartupMonitoringUnavailable, "ML-OS-001", MediaLockProblemSeverity.Warning),
        Define(MediaLockProblemId.CatalogStopped, "ML-CAT-001", MediaLockProblemSeverity.Error),
        Define(MediaLockProblemId.CatalogUnavailable, "ML-CAT-002", MediaLockProblemSeverity.Error),
        Define(MediaLockProblemId.CatalogTransitionFailed, "ML-CAT-003", MediaLockProblemSeverity.Error),
        Define(MediaLockProblemId.CommandFailed, "ML-CMD-001", MediaLockProblemSeverity.Error),
        Define(MediaLockProblemId.CommandRejected, "ML-CMD-002", MediaLockProblemSeverity.Warning),
        Define(MediaLockProblemId.CommandOutcomeUnknown, "ML-CMD-003", MediaLockProblemSeverity.Warning),
        Define(MediaLockProblemId.CommandUnsupported, "ML-CMD-004", MediaLockProblemSeverity.Warning),
        Define(MediaLockProblemId.CommandTargetUnavailable, "ML-CMD-005", MediaLockProblemSeverity.Warning),
        Define(MediaLockProblemId.SeekTimelineUnavailable, "ML-CMD-006", MediaLockProblemSeverity.Warning),
        Define(MediaLockProblemId.SeekOutOfRange, "ML-CMD-007", MediaLockProblemSeverity.Warning),
        Define(MediaLockProblemId.SeekNotConfirmed, "ML-CMD-008", MediaLockProblemSeverity.Warning),
        Define(MediaLockProblemId.SeekInterrupted, "ML-CMD-009", MediaLockProblemSeverity.Warning),
        Define(MediaLockProblemId.SettingsSaveFailed, "ML-SET-001", MediaLockProblemSeverity.Error),
        Define(MediaLockProblemId.SettingsPresentationApplyFailed, "ML-SET-002", MediaLockProblemSeverity.Error),
        Define(MediaLockProblemId.SupportActionFailed, "ML-SET-003", MediaLockProblemSeverity.Error),
        Define(MediaLockProblemId.RecoveryTimeoutInvalid, "ML-SET-004", MediaLockProblemSeverity.Warning),
        Define(MediaLockProblemId.RepeatedPauseWindowInvalid, "ML-SET-005", MediaLockProblemSeverity.Warning),
        Define(MediaLockProblemId.RepeatedPauseCountInvalid, "ML-SET-006", MediaLockProblemSeverity.Warning),
        Define(MediaLockProblemId.NotificationSoundFailed, "ML-UI-001", MediaLockProblemSeverity.Warning),
        Define(MediaLockProblemId.DiagnosticLoggingUnavailable, "ML-DIAG-001", MediaLockProblemSeverity.Warning),
        Define(MediaLockProblemId.MediaInputStartupFailed, "ML-INPUT-001", MediaLockProblemSeverity.Warning),
        Define(MediaLockProblemId.MediaInputStopped, "ML-INPUT-002", MediaLockProblemSeverity.Warning),
        Define(MediaLockProblemId.TargetAuthorizationRevokeFailed, "ML-BR-012", MediaLockProblemSeverity.Warning),
        Define(MediaLockProblemId.PlaybackCorrectionFailed, "ML-PLAY-001", MediaLockProblemSeverity.Warning),
    ];

    public static MediaLockProblemDefinition Get(MediaLockProblemId id) =>
        Definitions.FirstOrDefault(definition => definition.Id == id) ??
        Definitions[0];

    private static MediaLockProblemDefinition Define(
        MediaLockProblemId id,
        string code,
        MediaLockProblemSeverity severity) => new(id, code, severity);
}

public sealed record MediaLockProblem
{
    private static long nextOccurrenceId;

    private MediaLockProblem(
        MediaLockProblemId id,
        MediaLockProblemSeverity severity,
        long occurrenceId,
        string? exceptionType)
    {
        Id = id;
        Severity = severity;
        OccurrenceId = occurrenceId;
        ExceptionType = exceptionType;
    }

    public MediaLockProblemId Id { get; }

    public string Code => MediaLockProblemCatalog.Get(Id).Code;

    public MediaLockProblemSeverity Severity { get; }

    public long OccurrenceId { get; }

    public string? ExceptionType { get; }

    public static MediaLockProblem Create(
        MediaLockProblemId id,
        MediaLockProblemSeverity? severity = null,
        Exception? exception = null) => new(
            id,
            severity ?? MediaLockProblemCatalog.Get(id).DefaultSeverity,
            Interlocked.Increment(ref nextOccurrenceId),
            exception?.GetType().FullName ?? exception?.GetType().Name);

    public static MediaLockProblem Error(MediaLockProblemId id, Exception? exception = null) =>
        Create(id, MediaLockProblemSeverity.Error, exception);

    public static MediaLockProblem Warning(MediaLockProblemId id, Exception? exception = null) =>
        Create(id, MediaLockProblemSeverity.Warning, exception);
}
