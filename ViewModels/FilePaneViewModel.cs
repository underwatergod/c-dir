using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using CDir.Models;
using CDir.Services;

namespace CDir.ViewModels;

public sealed class FilePaneViewModel : INotifyPropertyChanged
{
    private CancellationTokenSource? _loadCts;

    private string _currentPath = "";
    private string _addressText = "";
    private string _filterText = "";
    private bool _isLoading;
    private bool _showHidden;
    private string _sortColumn = "Name";
    private bool _sortAscending = true;

    private readonly List<string> _history = [];
    private int _historyIndex = -1;

    public FilePaneViewModel(bool showHidden)
    {
        _showHidden = showHidden;
        Items = [];
        View = new ListCollectionView(Items)
        {
            Filter = FilterItem,
            CustomSort = new FileItemComparer(_sortColumn, _sortAscending),
        };
    }

    public ObservableCollection<FileItem> Items { get; }
    public ListCollectionView View { get; }

    /// <summary>Clickable path segments for the breadcrumb bar.</summary>
    public ObservableCollection<BreadcrumbSegment> Breadcrumbs { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<string>? PathChanged;

    public string CurrentPath => _currentPath;

    public string AddressText
    {
        get => _addressText;
        set => SetField(ref _addressText, value);
    }

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (!SetField(ref _filterText, value)) return;
            View.Refresh();
            OnPropertyChanged(nameof(FilterEmpty));
        }
    }

    public bool FilterEmpty => string.IsNullOrEmpty(_filterText);

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetField(ref _isLoading, value);
    }

    public bool ShowHidden
    {
        get => _showHidden;
        set
        {
            if (!SetField(ref _showHidden, value)) return;
            _ = RefreshAsync();
        }
    }

    public string SortColumn => _sortColumn;
    public bool SortAscending => _sortAscending;

    public bool CanGoBack => _historyIndex > 0;
    public bool CanGoForward => _historyIndex >= 0 && _historyIndex < _history.Count - 1;

    public async Task<bool> NavigateAsync(string? path, bool addHistory = true)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        if (!TryNormalize(path, out var target))
        {
            AddressText = _currentPath;
            return false;
        }

        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;

        IsLoading = true;
        try
        {
            var loaded = await Task.Run(() => LoadDirectory(target, _showHidden, cts.Token), cts.Token);
            if (cts.IsCancellationRequested) return false;

            Items.Clear();
            foreach (var item in loaded) Items.Add(item);

            _currentPath = target;
            AddressText = target;
            View.Refresh();
            UpdateBreadcrumbs(target);

            if (addHistory) PushHistory(target);

            OnPropertyChanged(nameof(CurrentPath));
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoForward));
            PathChanged?.Invoke(target);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            AddressText = _currentPath;
            MessageBox.Show($"폴더를 열 수 없습니다.\n{ex.Message}", "C-Dir", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        finally
        {
            if (_loadCts == cts) IsLoading = false;
        }
    }

    public async Task GoBackAsync()
    {
        if (!CanGoBack) return;
        var index = _historyIndex - 1;
        if (await NavigateAsync(_history[index], addHistory: false))
        {
            _historyIndex = index;
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoForward));
        }
    }

    public async Task GoForwardAsync()
    {
        if (!CanGoForward) return;
        var index = _historyIndex + 1;
        if (await NavigateAsync(_history[index], addHistory: false))
        {
            _historyIndex = index;
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoForward));
        }
    }

    public Task GoUpAsync()
    {
        var parent = Directory.GetParent(_currentPath)?.FullName;
        return parent is null ? Task.CompletedTask : NavigateAsync(parent);
    }

    public Task<bool> RefreshAsync() => NavigateAsync(_currentPath, addHistory: false);

    public Task<bool> GoToAsync(string path) => NavigateAsync(path);

    public void SortBy(string column)
    {
        if (string.Equals(_sortColumn, column, StringComparison.Ordinal))
            _sortAscending = !_sortAscending;
        else
        {
            _sortColumn = column;
            _sortAscending = true;
        }

        View.CustomSort = new FileItemComparer(_sortColumn, _sortAscending);
        OnPropertyChanged(nameof(SortColumn));
        OnPropertyChanged(nameof(SortAscending));
    }

    public FileItem? FindItem(string fullPath) =>
        Items.FirstOrDefault(i => string.Equals(i.FullPath, fullPath, StringComparison.OrdinalIgnoreCase));

    private static bool TryNormalize(string path, out string normalized)
    {
        normalized = "";
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            var full = Path.GetFullPath(expanded);
            if (full.Length > 3)
                full = full.TrimEnd(Path.DirectorySeparatorChar);
            if (!Directory.Exists(full)) return false;
            normalized = full;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void UpdateBreadcrumbs(string path)
    {
        Breadcrumbs.Clear();
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root))
            {
                Breadcrumbs.Add(new BreadcrumbSegment(path, path, true));
                return;
            }

            var segments = new List<BreadcrumbSegment>
            {
                new(root.TrimEnd(Path.DirectorySeparatorChar), root),
            };

            var remainder = path[root.Length..].Trim(Path.DirectorySeparatorChar);
            if (remainder.Length > 0)
            {
                var current = root.TrimEnd(Path.DirectorySeparatorChar);
                foreach (var part in remainder.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
                {
                    current = current + Path.DirectorySeparatorChar + part;
                    segments.Add(new BreadcrumbSegment(part, current));
                }
            }

            for (var i = 0; i < segments.Count; i++)
                Breadcrumbs.Add(segments[i] with { IsLast = i == segments.Count - 1 });
        }
        catch
        {
            Breadcrumbs.Add(new BreadcrumbSegment(path, path, true));
        }
    }

    private void PushHistory(string path)
    {        if (_historyIndex >= 0 && _historyIndex < _history.Count - 1)
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);

        if (_history.Count == 0 || !string.Equals(_history[^1], path, StringComparison.OrdinalIgnoreCase))
            _history.Add(path);

        _historyIndex = _history.Count - 1;
    }

    private static List<FileItem> LoadDirectory(string path, bool showHidden, CancellationToken cancellationToken)
    {
        var items = new List<FileItem>();
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
        };

        var directory = new DirectoryInfo(path);

        foreach (var child in directory.EnumerateDirectories("*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!showHidden && IsHidden(child.Attributes)) continue;

            try
            {
                items.Add(new FileItem
                {
                    Name = child.Name,
                    FullPath = child.FullName,
                    IsDirectory = true,
                    Modified = child.LastWriteTime,
                });
            }
            catch
            {
                // file vanished while enumerating
            }
        }

        foreach (var file in directory.EnumerateFiles("*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!showHidden && IsHidden(file.Attributes)) continue;

            try
            {
                items.Add(new FileItem
                {
                    Name = file.Name,
                    FullPath = file.FullName,
                    IsDirectory = false,
                    Size = file.Length,
                    Modified = file.LastWriteTime,
                    Extension = file.Extension,
                });
            }
            catch
            {
                // file vanished while enumerating
            }
        }

        return items;
    }

    private static bool IsHidden(FileAttributes attributes) =>
        (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;

    private bool FilterItem(object item)
    {
        if (item is not FileItem file) return false;
        if (string.IsNullOrWhiteSpace(_filterText)) return true;
        return file.Name.Contains(_filterText.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged(string? name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One clickable segment of the address breadcrumb.</summary>
public sealed record BreadcrumbSegment(string Name, string FullPath, bool IsLast = false);

internal sealed class FileItemComparer(string column, bool ascending) : IComparer
{
    public int Compare(object? x, object? y)
    {
        if (x is not FileItem a || y is not FileItem b) return 0;

        if (a.IsDirectory && b.IsDirectory)
        {
            var folderOrder = NaturalComparer.Compare(a.Name, b.Name);
            return column == "Name" && !ascending ? -folderOrder : folderOrder;
        }

        if (a.IsDirectory != b.IsDirectory)
            return a.IsDirectory ? -1 : 1;

        var result = column switch
        {
            "Size" => a.Size.CompareTo(b.Size),
            "Modified" => a.Modified.CompareTo(b.Modified),
            "Type" => string.Compare(a.TypeText, b.TypeText, StringComparison.CurrentCultureIgnoreCase),
            _ => NaturalComparer.Compare(a.Name, b.Name),
        };

        if (result == 0) result = NaturalComparer.Compare(a.Name, b.Name);
        return ascending ? result : -result;
    }
}
