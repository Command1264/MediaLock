using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MediaLock.App.ViewModels;

public sealed class MediaSourceGroupViewModel : INotifyPropertyChanged
{
    private bool isExpanded;
    private bool isSelected;

    internal MediaSourceGroupViewModel(
        string key,
        string displayName,
        string? sourceApplication,
        IReadOnlyList<BrowserTargetItemViewModel> browserTargets,
        IReadOnlyList<SessionItemViewModel> sessions,
        bool isExpanded)
    {
        Key = key;
        DisplayName = displayName;
        SourceApplication = sourceApplication;
        BrowserTargets = browserTargets;
        Sessions = sessions;
        this.isExpanded = isExpanded;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; }

    public string DisplayName { get; }

    public string? SourceApplication { get; }

    public IReadOnlyList<BrowserTargetItemViewModel> BrowserTargets { get; }

    public IReadOnlyList<SessionItemViewModel> Sessions { get; }

    public bool HasBrowserTargets => BrowserTargets.Count > 0;

    public bool HasSessions => Sessions.Count > 0;

    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (isExpanded == value)
            {
                return;
            }

            isExpanded = value;
            OnPropertyChanged();
        }
    }

    public bool IsSelected
    {
        get => isSelected;
        internal set
        {
            if (isSelected == value)
            {
                return;
            }

            isSelected = value;
            OnPropertyChanged();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
