using System.ComponentModel;
using System.Runtime.CompilerServices;
using MediaLock.App.Presentation;
using MediaLock.Core.Routing;

namespace MediaLock.App.ViewModels;

public sealed class PriorityRuleItemViewModel : INotifyPropertyChanged
{
    private bool isEnabled;
    private string displayName;
    private string details;

    internal PriorityRuleItemViewModel(
        PriorityRule rule,
        SourceApplicationPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(presentation);
        SourceAppUserModelId = rule.SourceAppUserModelId;
        displayName = presentation.DisplayName;
        details = presentation.Details;
        isEnabled = rule.IsEnabled;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SourceAppUserModelId { get; }

    public string DisplayName => displayName;

    public string Details => details;

    public bool IsEnabled
    {
        get => isEnabled;
        set
        {
            if (isEnabled == value)
            {
                return;
            }

            isEnabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
        }
    }

    public PriorityRule ToPriorityRule() => new(SourceAppUserModelId, IsEnabled);

    internal void ApplyPresentation(SourceApplicationPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        if (!string.Equals(displayName, presentation.DisplayName, StringComparison.Ordinal))
        {
            displayName = presentation.DisplayName;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
        }

        if (!string.Equals(details, presentation.Details, StringComparison.Ordinal))
        {
            details = presentation.Details;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Details)));
        }
    }
}
