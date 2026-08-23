using System.ComponentModel;
using System.Runtime.CompilerServices;
using MediaLock.Core.Routing;

namespace MediaLock.App.ViewModels;

public sealed class PriorityRuleItemViewModel : INotifyPropertyChanged
{
    private bool isEnabled;

    public PriorityRuleItemViewModel(PriorityRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        SourceAppUserModelId = rule.SourceAppUserModelId;
        isEnabled = rule.IsEnabled;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SourceAppUserModelId { get; }

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
}
