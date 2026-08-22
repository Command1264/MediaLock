using System.Collections.ObjectModel;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MediaLock.App.Localization;
using MediaLock.Application;
using MediaLock.Core.Configuration;
using MediaLock.Core.Routing;

namespace MediaLock.App.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IMediaLockApplication application;
    private readonly SynchronizationContext? synchronizationContext;
    private readonly Action? requestClose;
    private readonly Action<string>? applyLanguage;
    private bool closeToTray;
    private bool startWithWindows;
    private RoutingMode startupRoutingMode;
    private string startupRoutingModeText = UiText.Get("Mode_WindowsAuto");
    private string selectedLanguage = UiLanguagePreference.System;
    private double recoveryTimeoutSeconds;
    private FallbackPolicy fallbackPolicy;
    private string? errorMessage;
    private string? selectedAvailableApplication;
    private MediaLockSettings? appliedSettings;
    private bool disposed;

    public SettingsViewModel(
        IMediaLockApplication application,
        SynchronizationContext? synchronizationContext = null,
        Action? requestClose = null,
        Action<string>? applyLanguage = null)
    {
        ArgumentNullException.ThrowIfNull(application);
        this.application = application;
        this.synchronizationContext = synchronizationContext;
        this.requestClose = requestClose;
        this.applyLanguage = applyLanguage;
        SaveCommand = new AsyncCommand(_ => SaveAsync());
        AddPriorityRuleCommand = new AsyncCommand(AddPriorityRuleAsync);
        MovePriorityRuleUpCommand = new AsyncCommand(MovePriorityRuleUpAsync);
        MovePriorityRuleDownCommand = new AsyncCommand(MovePriorityRuleDownAsync);
        RemovePriorityRuleCommand = new AsyncCommand(RemovePriorityRuleAsync);
        application.StateChanged += OnApplicationStateChanged;
        UiText.CultureChanged += OnCultureChanged;
        RefreshLocalizedOptions();
        Apply(application.State.Settings);
        RefreshAvailableApplications(application.State);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<LocalizedOption<FallbackPolicy>> FallbackPolicies { get; private set; } = [];

    public IReadOnlyList<LocalizedOption<string>> Languages { get; private set; } = [];

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

    public string SelectedLanguage
    {
        get => selectedLanguage;
        set => SetField(ref selectedLanguage, value);
    }

    public string StartupRoutingModeText
    {
        get => startupRoutingModeText;
        private set => SetField(ref startupRoutingModeText, value);
    }

    public double RecoveryTimeoutSeconds
    {
        get => recoveryTimeoutSeconds;
        set => SetField(ref recoveryTimeoutSeconds, value);
    }

    public FallbackPolicy FallbackPolicy
    {
        get => fallbackPolicy;
        set => SetField(ref fallbackPolicy, value);
    }

    public IAsyncCommand SaveCommand { get; }

    public IAsyncCommand AddPriorityRuleCommand { get; }

    public IAsyncCommand MovePriorityRuleUpCommand { get; }

    public IAsyncCommand MovePriorityRuleDownCommand { get; }

    public IAsyncCommand RemovePriorityRuleCommand { get; }

    public string? ErrorMessage
    {
        get => errorMessage;
        private set => SetField(ref errorMessage, value);
    }

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
                new DesktopSettings(CloseToTray, StartWithWindows, SelectedLanguage),
                PriorityRules.Select(rule => rule.ToPriorityRule()).ToImmutableArray());
            await application.DispatchAsync(
                new ApplicationIntent.UpdateSettings(settings),
                CancellationToken.None);
            applyLanguage?.Invoke(SelectedLanguage);
            ErrorMessage = null;
            requestClose?.Invoke();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
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
        OnPropertyChanged(nameof(FallbackPolicies));
        OnPropertyChanged(nameof(Languages));
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
        SelectedLanguage = settings.Desktop.Language;
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
