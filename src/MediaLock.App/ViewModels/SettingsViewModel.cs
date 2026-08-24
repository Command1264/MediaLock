using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using MediaLock.App.Localization;
using MediaLock.Application;
using MediaLock.Core.Configuration;
using MediaLock.Core.Routing;

namespace MediaLock.App.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged, INotifyDataErrorInfo, IDisposable
{
    private readonly IMediaLockApplication application;
    private readonly SynchronizationContext? synchronizationContext;
    private readonly Action? requestClose;
    private readonly Action<string>? applyLanguage;
    private readonly Action<string>? applyTheme;
    private readonly AppEnvironmentInfo environmentInfo;
    private readonly IDesktopSupportActions? desktopSupportActions;
    private readonly Func<bool> isMediaInputRunning;
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
    private FallbackPolicy fallbackPolicy;
    private string? errorMessage;
    private string? supportStatusMessage;
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
        Func<bool>? isMediaInputRunning = null)
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

    public ObservableCollection<string> AvailableApplications { get; } = [];

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

    public bool HasErrors => recoveryTimeoutError is not null;

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

    public string? ErrorMessage
    {
        get => errorMessage;
        private set => SetField(ref errorMessage, value);
    }

    public IEnumerable GetErrors(string? propertyName) =>
        recoveryTimeoutError is not null &&
        (string.IsNullOrEmpty(propertyName) ||
         propertyName == nameof(RecoveryTimeoutText))
            ? new[] { recoveryTimeoutError }
            : Array.Empty<string>();

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        application.StateChanged -= OnApplicationStateChanged;
        UiText.CultureChanged -= OnCultureChanged;
        disposed = true;
    }

    private async Task SaveAsync()
    {
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
                PriorityRules.Select(rule => rule.ToPriorityRule()).ToImmutableArray());
            await application.DispatchAsync(
                new ApplicationIntent.UpdateSettings(settings),
                CancellationToken.None);
            applyLanguage?.Invoke(SelectedLanguage);
            applyTheme?.Invoke(SelectedTheme);
            ErrorMessage = null;
            requestClose?.Invoke();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
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
        ErrorMessage = null;
    }

    private void OnCultureChanged(object? sender, EventArgs args)
    {
        if (synchronizationContext is not null &&
            SynchronizationContext.Current != synchronizationContext)
        {
            synchronizationContext.Post(_ => RefreshLocalizedOptions(), null);
            return;
        }

        RefreshLocalizedOptions();
        OnPropertyChanged(nameof(ReleaseStatusText));
        if (HasErrors)
        {
            recoveryTimeoutError = UiText.Get("Settings_RecoveryTimeoutValidation");
            ErrorsChanged?.Invoke(
                this,
                new DataErrorsChangedEventArgs(nameof(RecoveryTimeoutText)));
        }
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
                    isMediaInputRunning())
                : null;
            await desktopSupportActions.ExecuteAsync(
                new DesktopSupportRequest(action, summary),
                CancellationToken.None);
            ErrorMessage = null;
            SupportStatusMessage = action == DesktopSupportAction.CopyDiagnostics
                ? UiText.Get("Settings_DiagnosticsCopied")
                : null;
        }
        catch (Exception exception)
        {
            SupportStatusMessage = null;
            ErrorMessage = UiText.Format("Settings_SupportActionFailed", exception.Message);
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
        PriorityRules.Clear();
        foreach (var rule in settings.PriorityRules)
        {
            PriorityRules.Add(new PriorityRuleItemViewModel(rule));
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
            : UiText.Get("Settings_RecoveryTimeoutValidation");

        if (isValid)
        {
            recoveryTimeoutSeconds = (double)seconds;
            OnPropertyChanged(nameof(RecoveryTimeoutSeconds));
        }

        if (!string.Equals(recoveryTimeoutError, nextError, StringComparison.Ordinal))
        {
            recoveryTimeoutError = nextError;
            OnPropertyChanged(nameof(HasErrors));
        }

        ErrorsChanged?.Invoke(
            this,
            new DataErrorsChangedEventArgs(nameof(RecoveryTimeoutText)));
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

        PriorityRules.Add(new PriorityRuleItemViewModel(
            new PriorityRule(SelectedAvailableApplication)));
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
        var available = state.Router.Sessions
            .Select(session => session.SourceAppUserModelId)
            .Distinct(StringComparer.Ordinal)
            .Where(source => !PriorityRules.Any(rule => string.Equals(
                rule.SourceAppUserModelId,
                source,
                StringComparison.Ordinal)))
            .OrderBy(source => source, StringComparer.Ordinal)
            .ToArray();
        if (AvailableApplications.SequenceEqual(available, StringComparer.Ordinal))
        {
            return;
        }

        var selected = SelectedAvailableApplication;
        AvailableApplications.Clear();
        foreach (var source in available)
        {
            AvailableApplications.Add(source);
        }

        SelectedAvailableApplication = selected is not null && available.Contains(
            selected,
            StringComparer.Ordinal)
                ? selected
                : null;
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
