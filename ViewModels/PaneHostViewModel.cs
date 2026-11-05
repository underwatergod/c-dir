using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace CDir.ViewModels;

/// <summary>One tab inside a pane: owns its own browser state and remembered selection.</summary>
public sealed class FileTab : INotifyPropertyChanged
{
    private string _header = "새 탭";
    private string _toolTip = "";
    private bool _isActive;

    public FileTab(FilePaneViewModel viewModel)
    {
        ViewModel = viewModel;
        ViewModel.PathChanged += _ => UpdateHeader();
        UpdateHeader();
    }

    public FilePaneViewModel ViewModel { get; }

    /// <summary>Paths selected when the tab was last active (restored on tab switch).</summary>
    public HashSet<string> SelectedPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string Header
    {
        get => _header;
        private set => SetField(ref _header, value);
    }

    public string ToolTip
    {
        get => _toolTip;
        private set => SetField(ref _toolTip, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetField(ref _isActive, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void UpdateHeader()
    {
        var path = ViewModel.CurrentPath;
        ToolTip = path;

        if (string.IsNullOrEmpty(path))
        {
            Header = "새 탭";
            return;
        }

        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        Header = string.IsNullOrEmpty(name) ? trimmed : name;
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>A pane is a host for one or more tabs; the tab strip and file list share this object.</summary>
public sealed class PaneHostViewModel : INotifyPropertyChanged
{
    private FileTab? _activeTab;
    private bool _showHidden;

    public PaneHostViewModel(bool showHidden)
    {
        _showHidden = showHidden;
        Tabs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CanCloseTabs));
    }

    public ObservableCollection<FileTab> Tabs { get; } = [];

    public FileTab? ActiveTab
    {
        get => _activeTab;
        private set
        {
            if (ReferenceEquals(_activeTab, value)) return;

            if (_activeTab is not null) _activeTab.IsActive = false;
            _activeTab = value;
            if (_activeTab is not null) _activeTab.IsActive = true;

            OnPropertyChanged(nameof(ActiveTab));
            OnPropertyChanged(nameof(Active));
            ActiveChanged?.Invoke();
        }
    }

    public FilePaneViewModel? Active => _activeTab?.ViewModel;

    public bool CanCloseTabs => Tabs.Count > 1;

    public IEnumerable<FilePaneViewModel> AllViewModels => Tabs.Select(t => t.ViewModel);

    public event Action? ActiveChanged;
    public event Action<string>? PathChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public FileTab AddTab(string path, bool activate = true)
    {
        var tab = new FileTab(new FilePaneViewModel(_showHidden));
        tab.ViewModel.PathChanged += changed =>
        {
            if (ReferenceEquals(tab, _activeTab)) PathChanged?.Invoke(changed);
        };

        Tabs.Add(tab);
        if (activate) ActiveTab = tab;

        _ = tab.ViewModel.NavigateAsync(path);
        return tab;
    }

    public void Activate(FileTab tab)
    {
        if (Tabs.Contains(tab)) ActiveTab = tab;
    }

    public void Close(FileTab tab)
    {
        if (Tabs.Count <= 1) return;

        var index = Tabs.IndexOf(tab);
        if (index < 0) return;

        var wasActive = ReferenceEquals(tab, _activeTab);
        Tabs.Remove(tab);

        if (wasActive)
            ActiveTab = Tabs[Math.Min(index, Tabs.Count - 1)];
    }

    public void Cycle(int delta)
    {
        if (Tabs.Count < 2 || _activeTab is null) return;

        var index = Tabs.IndexOf(_activeTab);
        ActiveTab = Tabs[(index + delta + Tabs.Count) % Tabs.Count];
    }

    public void SetShowHidden(bool value)
    {
        _showHidden = value;
        foreach (var viewModel in AllViewModels) viewModel.ShowHidden = value;
    }

    private void OnPropertyChanged(string? name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
