using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using MediaLock.App.Localization;
using MediaLock.App.Presentation;
using MediaLock.Application;
using MediaLock.Core.Configuration;
using MediaLock.Core.Routing;

namespace MediaLock.App.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged, INotifyDataErrorInfo, IDisposable
{
    private static readonly TimeSpan SupportStatusDuration = TimeSpan.FromSeconds(5);
    private readonly IMediaLockApplication application;
    private readonly SynchronizationContext? synchronizationContext;
    private readonly Action? requestClose;
    private readonly Action<string>? applyLanguage;
    private readonly Action<string>? applyTheme;
    private readonly AppEnvironmentInfo environmentInfo;
    private readonly IDesktopSupportActions? desktopSupportActions;
    private readonly Func<bool> isMediaInputRunning;
    private readonly ISourceApplicationMetadataResolver? sourceApplicationMetadataResolver;
    private readonly AsyncCommand saveCommand;
    private bool closeToTray;
    private bool startWithWindows;
    private bool interceptMediaKeys;
    private RoutingMode startupRoutingMode;
    private string startupRoutingModeText = UiText.Get("Mode_WindowsAuto");
    private string selectedLanguage = UiLanguagePreference.System;
    private string selectedTheme = UiThemePreference.System;
    private double recoveryTimeoutSeconds;
    private string recoveryTimeoutText = string.Empty;
    private string? recoveryTimeoutError;
    private bool repeatedPauseOverrideEnabled;
    private int repeatedPauseWindowSeconds;
    private string repeatedPauseWindowText = string.Empty;
    private string? repeatedPauseWindowError;
    private int repeatedPauseCount;
    private string repeatedPauseCountText = string.Empty;
    private string? repeatedPauseCountError;
    private bool playRepeatedPauseOverrideSound;
    private FallbackPolicy fallbackPolicy;
    private MediaLockProblem? errorProblem;
    private string? supportStatusMessage;
    private CancellationTokenSource? supportStatusCancellation;
    private string? selectedAvailableApplication;
    private MediaLockSettings? appliedSettings;
    private bool disposed;

    public SettingsViewModel(
        IMediaLockApplication application,
        SynchronizationContext? synchronizationContext = null,
        Action? requestClose = null,
        Action<string>? applyLanguage = null,
        Action<string>? applyTheme = null,
        IAppEnvironmentInfoProvider? environmentInfoProvider = null,
        IDesktopSupportActions? desktopSupportActions = null,
        Func<bool>? isMediaInputRunning = null,
        ISourceApplicationMetadataResolver? sourceApplicationMetadataResolver = null)
    {
        ArgumentNullException.ThrowIfNull(application);
        this.application = application;
        this.synchronizationContext = synchronizationContext;
        this.requestClose = requestClose;
        this.applyLanguage = applyLanguage;
        this.applyTheme = applyTheme;
        environmentInfo = environmentInfoProvider?.GetCurrent() ?? new AppEnvironmentInfo(
            "Unknown",
            "Windows",
            string.Empty,
            "Unknown",
            "Unknown",
            IsSigned: false);
        this.desktopSupportActions = desktopSupportActions;
        this.isMediaInputRunning = isMediaInputRunning ?? (() => false);
        this.sourceApplicationMetadataResolver = sourceApplicationMetadataResolver;
        saveCommand = new AsyncCommand(_ => SaveAsync(), _ => !HasErrors);
        SaveCommand = saveCommand;
        CancelCommand = new AsyncCommand(_ => CancelAsync());
        AddPriorityRuleCommand = new AsyncCommand(AddPriorityRuleAsync);
        MovePriorityRuleUpCommand = new AsyncCommand(MovePriorityRuleUpAsync);
        MovePriorityRuleDownCommand = new AsyncCommand(MovePriorityRuleDownAsync);
        RemovePriorityRuleCommand = new AsyncCommand(RemovePriorityRuleAsync);
        CopyDiagnosticsCommand = new AsyncCommand(
            _ => ExecuteSupportActionAsync(DesktopSupportAction.CopyDiagnostics),
            _ => desktopSupportActions is not null);
        OpenLogsFolderCommand = new AsyncCommand(
            _ => ExecuteSupportActionAsync(DesktopSupportAction.OpenLogsFolder),
            _ => desktopSupportActions is not null);
        OpenSupportCommand = new AsyncCommand(
            _ => ExecuteSupportActionAsync(DesktopSupportAction.OpenSupport),
            _ => desktopSupportActions is not null);
        ReportBugCommand = new AsyncCommand(
            _ => ExecuteSupportActionAsync(DesktopSupportAction.ReportBug),
            _ => desktopSupportActions is not null);
        application.StateChanged += OnApplicationStateChanged;
        UiText.CultureChanged += OnCultureChanged;
        RefreshLocalizedOptions();
        Apply(application.State.Settings);
        RefreshAvailableApplications(application.State);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public IReadOnlyList<LocalizedOption<FallbackPolicy>> FallbackPolicies { get; private set; } = [];

    public IReadOnlyList<LocalizedOption<string>> Languages { get; private set; } = [];

    public IReadOnlyList<LocalizedOption<string>> Themes { get; private set; } = [];

    public ObservableCollection<PriorityRuleItemViewModel> PriorityRules { get; } = [];

    public ObservableCollection<SourceApplicationOptionViewModel> AvailableApplications { get; } = [];

    public string? SelectedAvailableApplication
    {
        get => selectedAvailableApplication;
        set => SetField(ref selectedAvailableApplication, value);
    }

    public bool CloseToTray
    {
        get => closeToTray;
        set => SetField(ref closeToTray, value);
    }

    public bool StartWithWindows
    {
        get => startWithWindows;
        set => SetField(ref startWithWindows, value);
    }

    public bool InterceptMediaKeys
    {
        get => interceptMediaKeys;
        set => SetField(ref interceptMediaKeys, value);
    }

    public string SelectedLanguage
    {
        get => selectedLanguage;
        set
        {
            if (selectedLanguage != value)
            {
                SetField(ref selectedLanguage, value);
                OnPropertyChanged(nameof(SelectedLanguageOption));
            }
        }
    }

    public LocalizedOption<string>? SelectedLanguageOption
    {
        get => Languages.FirstOrDefault(option => option.Value == SelectedLanguage);
        set
        {
            if (value is not null)
            {
                SelectedLanguage = value.Value;
            }
        }
    }

    public string SelectedTheme
    {
        get => selectedTheme;
        set
        {
            if (selectedTheme != value)
            {
                SetField(ref selectedTheme, value);
                OnPropertyChanged(nameof(SelectedThemeOption));
            }
        }
    }

    public LocalizedOption<string>? SelectedThemeOption
    {
        get => Themes.FirstOrDefault(option => option.Value == SelectedTheme);
        set
        {
            if (value is not null)
            {
                SelectedTheme = value.Value;
            }
        }
    }

    public string StartupRoutingModeText
    {
        get => startupRoutingModeText;
        private set => SetField(ref startupRoutingModeText, value);
    }

    public double RecoveryTimeoutSeconds
    {
        get => recoveryTimeoutSeconds;
        set => RecoveryTimeoutText = value.ToString(UiText.CurrentCulture);
    }

    public string RecoveryTimeoutText
    {
        get => recoveryTimeoutText;
        set
        {
            value ??= string.Empty;
            if (string.Equals(recoveryTimeoutText, value, StringComparison.Ordinal))
            {
                return;
            }

            recoveryTimeoutText = value;
            OnPropertyChanged();
            ValidateRecoveryTimeout();
        }
    }

    public bool RepeatedPauseOverrideEnabled
    {
        get => repeatedPauseOverrideEnabled;
        set => SetField(ref repeatedPauseOverrideEnabled, value);
    }

    public string RepeatedPauseWindowText
    {
        get => repeatedPauseWindowText;
        set
        {
            value ??= string.Empty;
            if (!string.Equals(repeatedPauseWindowText, value, StringComparison.Ordinal))
            {
                repeatedPauseWindowText = value;
                OnPropertyChanged();
                ValidateRepeatedPauseWindow();
            }
        }
    }

    public string RepeatedPauseCountText
    {
        get => repeatedPauseCountText;
        set
        {
            value ??= string.Empty;
            if (!string.Equals(repeatedPauseCountText, value, StringComparison.Ordinal))
            {
                repeatedPauseCountText = value;
                OnPropertyChanged();
                ValidateRepeatedPauseCount();
            }
        }
    }

    public bool PlayRepeatedPauseOverrideSound
    {
        get => playRepeatedPauseOverrideSound;
        set => SetField(ref playRepeatedPauseOverrideSound, value);
    }

    public bool HasErrors => recoveryTimeoutError is not null ||
        repeatedPauseWindowError is not null || repeatedPauseCountError is not null;

    public FallbackPolicy FallbackPolicy
    {
        get => fallbackPolicy;
        set => SetField(ref fallbackPolicy, value);
    }

    public IAsyncCommand SaveCommand { get; }

    public IAsyncCommand CancelCommand { get; }

    public IAsyncCommand AddPriorityRuleCommand { get; }

    public IAsyncCommand MovePriorityRuleUpCommand { get; }

    public IAsyncCommand MovePriorityRuleDownCommand { get; }

    public IAsyncCommand RemovePriorityRuleCommand { get; }

    public IAsyncCommand CopyDiagnosticsCommand { get; }

    public IAsyncCommand OpenLogsFolderCommand { get; }

    public IAsyncCommand OpenSupportCommand { get; }

    public IAsyncCommand ReportBugCommand { get; }

    public string AppVersion => environmentInfo.AppVersion;

    public string WindowsDescription
    {
        get
        {
            var displayVersion = string.IsNullOrWhiteSpace(environmentInfo.WindowsDisplayVersion)
                ? string.Empty
                : $" {environmentInfo.WindowsDisplayVersion}";
            return $"{environmentInfo.WindowsProductName}{displayVersion} · " +
                $"{environmentInfo.WindowsBuild} · {environmentInfo.Architecture}";
        }
    }

    public string ReleaseStatusText => UiText.Get(environmentInfo.IsPrerelease
        ? "Settings_ReleasePrerelease"
        : "Settings_ReleaseStable") + " · " + UiText.Get(environmentInfo.IsSigned
            ? "Settings_SignatureSigned"
            : "Settings_SignatureUnsigned");

    public string? SupportStatusMessage
    {
        get => supportStatusMessage;
        private set => SetField(ref supportStatusMessage, value);
    }

    public string? ErrorMessage => errorProblem is not null
        ? ProblemPresentation.Describe(errorProblem)
        : recoveryTimeoutError ?? repeatedPauseWindowError ?? repeatedPauseCountError;

    public IEnumerable GetErrors(string? propertyName)
    {
        var errors = new List<string>(3);
        if (recoveryTimeoutError is not null &&
            (string.IsNullOrEmpty(propertyName) || propertyName == nameof(RecoveryTimeoutText)))
        {
            errors.Add(recoveryTimeoutError);
        }

        if (repeatedPauseWindowError is not null &&
            (string.IsNullOrEmpty(propertyName) || propertyName == nameof(RepeatedPauseWindowText)))
        {
            errors.Add(repeatedPauseWindowError);
        }

        if (repeatedPauseCountError is not null &&
            (string.IsNullOrEmpty(propertyName) || propertyName == nameof(RepeatedPauseCountText)))
        {
            errors.Add(repeatedPauseCountError);
        }

        return errors;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        application.StateChanged -= OnApplicationStateChanged;
        UiText.CultureChanged -= OnCultureChanged;
        ClearSupportStatus();
        disposed = true;
    }

    private async Task SaveAsync()
    {
        var previousSettings = application.State.Settings;
        var presentationFailed = false;
        try
        {
            var settings = new MediaLockSettings(
                MediaLockSettings.CurrentSchemaVersion,
                startupRoutingMode,
                new RecoverySettings(
                    TimeSpan.FromSeconds(RecoveryTimeoutSeconds),
                    FallbackPolicy),
                new DesktopSettings(
                    CloseToTray,
                    StartWithWindows,
                    SelectedLanguage,
                    SelectedTheme,
                    InterceptMediaKeys),
                PriorityRules.Select(rule => rule.ToPriorityRule()).ToImmutableArray())
            {
                PlaybackStateLock = new PlaybackStateLockSettings(
                    RepeatedPauseOverrideEnabled,
                    TimeSpan.FromSeconds(repeatedPauseWindowSeconds),
                    repeatedPauseCount,
                    PlayRepeatedPauseOverrideSound),
            };
            await application.DispatchAsync(
                new ApplicationIntent.UpdateSettings(settings),
                CancellationToken.None);
            try
            {
                applyLanguage?.Invoke(SelectedLanguage);
                applyTheme?.Invoke(SelectedTheme);
            }
            catch (Exception exception)
            {
                presentationFailed = true;
                var failures = new List<Exception> { exception };
                try
                {
                    await application.DispatchAsync(
                        new ApplicationIntent.UpdateSettings(previousSettings),
                        CancellationToken.None);
                }
                catch (Exception rollbackException)
                {
                    failures.Add(rollbackException);
                }

                try
                {
                    applyLanguage?.Invoke(previousSettings.Desktop!.Language);
                }
                catch (Exception rollbackException)
                {
                    failures.Add(rollbackException);
                }

                try
                {
                    applyTheme?.Invoke(previousSettings.Desktop!.Theme);
                }
                catch (Exception rollbackException)
                {
                    failures.Add(rollbackException);
                }

                throw new AggregateException(
                    "Settings presentation could not be applied; rollback was attempted.",
                    failures);
            }

            SetProblem(null);
            ClearSupportStatus();
            requestClose?.Invoke();
        }
        catch (Exception exception)
        {
            SetProblem(MediaLockProblem.Error(
                presentationFailed
                    ? MediaLockProblemId.SettingsPresentationApplyFailed
                    : MediaLockProblemId.SettingsSaveFailed,
                exception));
        }
    }

    private Task CancelAsync()
    {
        DiscardChanges();
        requestClose?.Invoke();
        return Task.CompletedTask;
    }

    internal void DiscardChanges()
    {
        Apply(application.State.Settings);
        SetProblem(null);
        ClearSupportStatus();
    }

    private void OnCultureChanged(object? sender, EventArgs args)
    {
        if (synchronizationContext is not null &&
            SynchronizationContext.Current != synchronizationContext)
        {
            synchronizationContext.Post(_ => RefreshLocalizedPresentation(), null);
            return;
        }

        RefreshLocalizedPresentation();
    }

    private void RefreshLocalizedPresentation()
    {
        RefreshLocalizedOptions();
        OnPropertyChanged(nameof(ReleaseStatusText));
        if (SupportStatusMessage is not null)
        {
            SupportStatusMessage = UiText.Get("Settings_DiagnosticsCopied");
        }

        if (HasErrors)
        {
            if (recoveryTimeoutError is not null)
            {
                recoveryTimeoutError = DescribeValidation(MediaLockProblemId.RecoveryTimeoutInvalid);
                ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(RecoveryTimeoutText)));
            }

            if (repeatedPauseWindowError is not null)
            {
                repeatedPauseWindowError = DescribeValidation(
                    MediaLockProblemId.RepeatedPauseWindowInvalid);
                ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(RepeatedPauseWindowText)));
            }

            if (repeatedPauseCountError is not null)
            {
                repeatedPauseCountError = DescribeValidation(
                    MediaLockProblemId.RepeatedPauseCountInvalid);
                ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(RepeatedPauseCountText)));
            }
        }

        OnPropertyChanged(nameof(ErrorMessage));
    }

    private async Task ExecuteSupportActionAsync(DesktopSupportAction action)
    {
        if (desktopSupportActions is null)
        {
            return;
        }

        try
        {
            var summary = action == DesktopSupportAction.CopyDiagnostics
                ? DiagnosticSummary.Create(
                    environmentInfo,
                    application.State,
                    isMediaInputRunning(),
                    application.LastReportedProblemCode)
                : null;
            await desktopSupportActions.ExecuteAsync(
                new DesktopSupportRequest(action, summary),
                CancellationToken.None);
            SetProblem(null);
            if (action == DesktopSupportAction.CopyDiagnostics)
            {
                ShowSupportStatus(UiText.Get("Settings_DiagnosticsCopied"));
            }
            else
            {
                ClearSupportStatus();
            }
        }
        catch (Exception exception)
        {
            ClearSupportStatus();
            SetProblem(MediaLockProblem.Error(
                MediaLockProblemId.SupportActionFailed,
                exception));
        }
    }

    private void ShowSupportStatus(string message)
    {
        ClearSupportStatus();
        SupportStatusMessage = message;
        var cancellation = new CancellationTokenSource();
        supportStatusCancellation = cancellation;
        _ = ClearSupportStatusAfterDelayAsync(cancellation);
    }

    private void ClearSupportStatus()
    {
        var cancellation = supportStatusCancellation;
        supportStatusCancellation = null;
        cancellation?.Cancel();
        SupportStatusMessage = null;
    }

    private void SetProblem(MediaLockProblem? problem)
    {
        if (Equals(errorProblem, problem))
        {
            return;
        }

        errorProblem = problem;
        if (problem is not null)
        {
            _ = application.ReportProblemAsync(problem, CancellationToken.None);
        }
        OnPropertyChanged(nameof(ErrorMessage));
    }

    private string DescribeValidation(MediaLockProblemId id)
    {
        var problem = MediaLockProblem.Warning(id);
        _ = application.ReportProblemAsync(problem, CancellationToken.None);
        return ProblemPresentation.Describe(problem);
    }

    private async Task ClearSupportStatusAfterDelayAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(SupportStatusDuration, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (ReferenceEquals(supportStatusCancellation, cancellation))
            {
                supportStatusCancellation = null;
                SupportStatusMessage = null;
            }

            cancellation.Dispose();
        }
    }

    private void RefreshLocalizedOptions()
    {
        FallbackPolicies = Enum.GetValues<FallbackPolicy>()
            .Select(value => new LocalizedOption<FallbackPolicy>(
                value,
                UiDescriptions.DescribeFallbackPolicy(value)))
            .ToArray();
        Languages =
        [
            new(UiLanguagePreference.System, UiText.Get("Language_System")),
            new(UiLanguagePreference.EnglishUnitedStates, UiText.Get("Language_English")),
            new(UiLanguagePreference.TraditionalChinese, UiText.Get("Language_TraditionalChinese")),
        ];
        Themes =
        [
            new(UiThemePreference.System, UiText.Get("Theme_System")),
            new(UiThemePreference.Light, UiText.Get("Theme_Light")),
            new(UiThemePreference.Dark, UiText.Get("Theme_Dark")),
        ];
        OnPropertyChanged(nameof(FallbackPolicies));
        OnPropertyChanged(nameof(Languages));
        OnPropertyChanged(nameof(Themes));
        OnPropertyChanged(nameof(SelectedLanguageOption));
        OnPropertyChanged(nameof(SelectedThemeOption));
        ApplyStartupRoutingMode(startupRoutingMode);
    }

    private void OnApplicationStateChanged(
        object? sender,
        MediaLockApplicationStateChangedEventArgs args)
    {
        if (synchronizationContext is not null &&
            SynchronizationContext.Current != synchronizationContext)
        {
            synchronizationContext.Post(_ =>
            {
                RefreshAvailableApplications(args.State);
                ApplyIfChanged(args.State.Settings);
            }, null);
            return;
        }

        RefreshAvailableApplications(args.State);
        ApplyIfChanged(args.State.Settings);
    }

    private void ApplyIfChanged(MediaLockSettings settings)
    {
        if (Equals(appliedSettings, settings))
        {
            return;
        }

        if (appliedSettings is { } previous &&
            Equals(settings with { DefaultRoutingMode = previous.DefaultRoutingMode }, previous))
        {
            appliedSettings = settings;
            ApplyStartupRoutingMode(settings.DefaultRoutingMode);
            return;
        }

        Apply(settings);
    }

    private void Apply(MediaLockSettings settings)
    {
        appliedSettings = settings;
        CloseToTray = settings.Desktop!.CloseToTray;
        StartWithWindows = settings.Desktop.StartWithWindows;
        InterceptMediaKeys = settings.Desktop.InterceptMediaKeys;
        SelectedLanguage = settings.Desktop.Language;
        SelectedTheme = settings.Desktop.Theme;
        ApplyStartupRoutingMode(settings.DefaultRoutingMode);
        RecoveryTimeoutSeconds = settings.Recovery!.Timeout.TotalSeconds;
        FallbackPolicy = settings.Recovery.FallbackPolicy;
        var playbackSettings = settings.PlaybackStateLock!;
        RepeatedPauseOverrideEnabled = playbackSettings.RepeatedPauseOverrideEnabled;
        RepeatedPauseWindowText = ((int)playbackSettings.RepeatedPauseWindow.TotalSeconds)
            .ToString(CultureInfo.InvariantCulture);
        RepeatedPauseCountText = playbackSettings.RepeatedPauseCount
            .ToString(CultureInfo.InvariantCulture);
        PlayRepeatedPauseOverrideSound = playbackSettings.PlayOverrideSound;
        var presentations = ResolvePresentations(
            application.State,
            settings.PriorityRules.Select(rule => rule.SourceAppUserModelId));
        PriorityRules.Clear();
        foreach (var rule in settings.PriorityRules)
        {
            PriorityRules.Add(new PriorityRuleItemViewModel(
                rule,
                presentations[rule.SourceAppUserModelId]));
        }

        RefreshAvailableApplications(application.State);
    }

    private void ApplyStartupRoutingMode(RoutingMode mode)
    {
        startupRoutingMode = mode;
        StartupRoutingModeText = UiDescriptions.DescribeRoutingMode(mode);
    }

    private void ValidateRecoveryTimeout()
    {
        const NumberStyles styles =
            NumberStyles.AllowLeadingWhite |
            NumberStyles.AllowTrailingWhite |
            NumberStyles.AllowDecimalPoint;
        var isValid = decimal.TryParse(
                RecoveryTimeoutText,
                styles,
                UiText.CurrentCulture,
                out var seconds) &&
            seconds >= RecoverySettings.MinimumTimeoutSeconds &&
            seconds <= RecoverySettings.MaximumTimeoutSeconds;
        var nextError = isValid
            ? null
            : DescribeValidation(MediaLockProblemId.RecoveryTimeoutInvalid);

        if (isValid)
        {
            recoveryTimeoutSeconds = (double)seconds;
            OnPropertyChanged(nameof(RecoveryTimeoutSeconds));
        }

        if (!string.Equals(recoveryTimeoutError, nextError, StringComparison.Ordinal))
        {
            recoveryTimeoutError = nextError;
            OnPropertyChanged(nameof(HasErrors));
            OnPropertyChanged(nameof(ErrorMessage));
        }

        ErrorsChanged?.Invoke(
            this,
            new DataErrorsChangedEventArgs(nameof(RecoveryTimeoutText)));
        saveCommand.RaiseCanExecuteChanged();
    }

    private void ValidateRepeatedPauseWindow()
    {
        var isValid = int.TryParse(
                RepeatedPauseWindowText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var seconds) &&
            seconds is >= PlaybackStateLockSettings.MinimumWindowSeconds and
                <= PlaybackStateLockSettings.MaximumWindowSeconds;
        if (isValid)
        {
            repeatedPauseWindowSeconds = seconds;
        }

        SetValidationError(
            nameof(RepeatedPauseWindowText),
            ref repeatedPauseWindowError,
            isValid ? null : DescribeValidation(MediaLockProblemId.RepeatedPauseWindowInvalid));
    }

    private void ValidateRepeatedPauseCount()
    {
        var isValid = int.TryParse(
                RepeatedPauseCountText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var count) &&
            count is >= PlaybackStateLockSettings.MinimumPauseCount and
                <= PlaybackStateLockSettings.MaximumPauseCount;
        if (isValid)
        {
            repeatedPauseCount = count;
        }

        SetValidationError(
            nameof(RepeatedPauseCountText),
            ref repeatedPauseCountError,
            isValid ? null : DescribeValidation(MediaLockProblemId.RepeatedPauseCountInvalid));
    }

    private void SetValidationError(string propertyName, ref string? field, string? next)
    {
        if (!string.Equals(field, next, StringComparison.Ordinal))
        {
            field = next;
            OnPropertyChanged(nameof(HasErrors));
            OnPropertyChanged(nameof(ErrorMessage));
        }

        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
        saveCommand.RaiseCanExecuteChanged();
    }

    private Task AddPriorityRuleAsync(object? parameter)
    {
        if (string.IsNullOrWhiteSpace(SelectedAvailableApplication) ||
            PriorityRules.Any(rule => string.Equals(
                rule.SourceAppUserModelId,
                SelectedAvailableApplication,
                StringComparison.Ordinal)))
        {
            return Task.CompletedTask;
        }

        var presentation = ResolvePresentations(
            application.State,
            [SelectedAvailableApplication])[SelectedAvailableApplication];
        PriorityRules.Add(new PriorityRuleItemViewModel(
            new PriorityRule(SelectedAvailableApplication),
            presentation));
        SelectedAvailableApplication = null;
        RefreshAvailableApplications(application.State);
        return Task.CompletedTask;
    }

    private Task MovePriorityRuleUpAsync(object? parameter)
    {
        if (parameter is PriorityRuleItemViewModel rule)
        {
            var index = PriorityRules.IndexOf(rule);
            if (index > 0)
            {
                PriorityRules.Move(index, index - 1);
            }
        }

        return Task.CompletedTask;
    }

    private Task MovePriorityRuleDownAsync(object? parameter)
    {
        if (parameter is PriorityRuleItemViewModel rule)
        {
            var index = PriorityRules.IndexOf(rule);
            if (index >= 0 && index < PriorityRules.Count - 1)
            {
                PriorityRules.Move(index, index + 1);
            }
        }

        return Task.CompletedTask;
    }

    private Task RemovePriorityRuleAsync(object? parameter)
    {
        if (parameter is PriorityRuleItemViewModel rule)
        {
            PriorityRules.Remove(rule);
            RefreshAvailableApplications(application.State);
        }

        return Task.CompletedTask;
    }

    private void RefreshAvailableApplications(MediaLockApplicationState state)
    {
        var presentations = ResolvePresentations(state);
        foreach (var rule in PriorityRules)
        {
            rule.ApplyPresentation(presentations[rule.SourceAppUserModelId]);
        }

        var availableSourceIds = state.Router.Sessions
            .Select(session => session.SourceAppUserModelId)
            .Distinct(StringComparer.Ordinal)
            .Where(source => !PriorityRules.Any(rule => string.Equals(
                rule.SourceAppUserModelId,
                source,
                StringComparison.Ordinal)))
            .ToArray();
        var available = availableSourceIds
            .Select(source => new SourceApplicationOptionViewModel(
                source,
                presentations[source].DisplayName,
                presentations[source].Details))
            .OrderBy(option => option.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(option => option.SourceAppUserModelId, StringComparer.Ordinal)
            .ToArray();
        if (AvailableApplications.SequenceEqual(available))
        {
            return;
        }

        var selected = SelectedAvailableApplication;
        AvailableApplications.Clear();
        foreach (var source in available)
        {
            AvailableApplications.Add(source);
        }

        SelectedAvailableApplication = selected is not null && availableSourceIds.Contains(
            selected,
            StringComparer.Ordinal)
                ? selected
                : null;
    }

    private IReadOnlyDictionary<string, SourceApplicationPresentation> ResolvePresentations(
        MediaLockApplicationState state,
        IEnumerable<string>? extraSourceIds = null)
    {
        var sourceIds = state.Router.Sessions
            .Select(session => session.SourceAppUserModelId)
            .Concat(PriorityRules.Select(rule => rule.SourceAppUserModelId));
        if (extraSourceIds is not null)
        {
            sourceIds = sourceIds.Concat(extraSourceIds);
        }

        return SourceApplicationPresentationCatalog.Resolve(
            sourceIds,
            sourceApplicationMetadataResolver);
    }

    private void SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
