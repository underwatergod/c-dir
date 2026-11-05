using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using CDir.Controls;
using CDir.Models;
using CDir.Services;
using CDir.ViewModels;

namespace CDir;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly SearchService _searchService;
    private readonly SearchViewModel _search;
    private readonly List<FilePane> _panes = [];
    private readonly List<PaneHostViewModel> _hosts = [];

    private FilePane? _activePane;
    private CancellationTokenSource? _indexCts;
    private bool _indexing;
    private bool _layoutSync;

    public MainWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        FavoritesStore.Initialize(settings.Favorites);

        _searchService = new SearchService(new NativeFileIndex(), new EverythingSearchService());
        _search = new SearchViewModel(_searchService);
        SearchArea.DataContext = _search;
        ResultsPanel.DataContext = _search;

        _panes.AddRange([Pane0, Pane1, Pane2, Pane3]);
        foreach (var pane in _panes)
        {
            var host = new PaneHostViewModel(settings.ShowHidden);
            pane.Attach(host);
            pane.Activated += (sender, _) => SetActivePane((FilePane)sender!);
            pane.PaneStatusChanged += (_, _) => UpdateStatusBar();
            pane.PanePathChanged += (_, _) => UpdateStatusBar();
            _hosts.Add(host);
        }

        _searchService.Index.Progress += count => Dispatcher.BeginInvoke(new Action(() =>
        {
            IndexStatusText.Text = $"인덱싱 중… {count:N0}개";
        }));

        _searchService.Index.Completed += count => Dispatcher.BeginInvoke(new Action(() =>
        {
            _indexing = false;
            CancelIndexButton.Visibility = Visibility.Collapsed;
            IndexStatusText.Text = $"내장 인덱스 {count:N0}개";
        }));

        _searchService.Index.Failed += message => Dispatcher.BeginInvoke(new Action(() =>
        {
            _indexing = false;
            CancelIndexButton.Visibility = Visibility.Collapsed;
            IndexStatusText.Text = $"인덱스 실패: {message}";
        }));

        _search.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(SearchViewModel.ProviderName)) return;

            UpdateProviderChip();
            if (!_searchService.EverythingAvailable && !_searchService.Index.HasData && !_indexing)
                _ = StartIndexingAsync();
        };

        RestoreWindow(settings);
        RestorePanes(settings);
        ArrangeLayout(settings.PaneCount);

        UpdateProviderChip();
        UpdateStatusBar();
        ThemeButton.Content = ThemeManager.IsDark ? "\uE706" : "\uE708";

        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    // ---------- startup ----------

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        var hadCache = await _searchService.Index.LoadCacheAsync();
        if (hadCache)
            IndexStatusText.Text = $"내장 인덱스 {_searchService.Index.Count:N0}개";
        else if (_searchService.EverythingAvailable)
            IndexStatusText.Text = "Everything 검색 사용 중";
        else
            IndexStatusText.Text = "내장 인덱스 없음";

        if (!_searchService.EverythingAvailable && !_searchService.Index.HasData)
            await StartIndexingAsync();
        else
        {
            UpdateProviderChip();
            UpdateStatusBar();
        }
    }

    private void RestoreWindow(AppSettings settings)
    {
        if (settings.WindowWidth >= 400) Width = settings.WindowWidth;
        if (settings.WindowHeight >= 300) Height = settings.WindowHeight;

        if (settings.WindowLeft is { } left && settings.WindowTop is { } top)
        {
            var virtualScreen = new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);

            if (virtualScreen.Contains(new Point(left + 80, top + 40)))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = left;
                Top = top;
            }
        }

        if (settings.Maximized) WindowState = WindowState.Maximized;
    }

    private void RestorePanes(AppSettings settings)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var defaults = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(userProfile, "Downloads"),
            userProfile,
            "C:\\",
        };

        for (var i = 0; i < _hosts.Count; i++)
        {
            var paths = i < settings.PaneTabs.Count && settings.PaneTabs[i].Count > 0
                ? settings.PaneTabs[i]
                    .Where(Directory.Exists)
                    .Take(12)
                    .ToList()
                : [];

            if (paths.Count == 0)
            {
                var fallback = i < settings.PanePaths.Count && Directory.Exists(settings.PanePaths[i])
                    ? settings.PanePaths[i]
                    : defaults[i];
                paths.Add(fallback);
            }

            foreach (var path in paths)
                _hosts[i].AddTab(path, activate: false);

            if (_hosts[i].Tabs.Count == 0) continue;

            var activeIndex = i < settings.PaneActiveTab.Count ? settings.PaneActiveTab[i] : 0;
            _hosts[i].Activate(_hosts[i].Tabs[Math.Clamp(activeIndex, 0, _hosts[i].Tabs.Count - 1)]);
        }
    }

    // ---------- layout ----------

    private void OnLayoutChecked(object sender, RoutedEventArgs e)
    {
        if (_layoutSync || sender is not RadioButton { Tag: string tag } || !int.TryParse(tag, out var count)) return;
        ArrangeLayout(count);
    }

    private void ArrangeLayout(int count)
    {
        count = Math.Clamp(count, 1, 4);
        _settings.PaneCount = count;

        _layoutSync = true;
        Layout1.IsChecked = count == 1;
        Layout2.IsChecked = count == 2;
        Layout3.IsChecked = count == 3;
        Layout4.IsChecked = count == 4;
        _layoutSync = false;

        PaneHost.RowDefinitions.Clear();
        PaneHost.ColumnDefinitions.Clear();

        foreach (var pane in _panes)
        {
            Grid.SetRow(pane, 0);
            Grid.SetColumn(pane, 0);
            Grid.SetRowSpan(pane, 1);
            Grid.SetColumnSpan(pane, 1);
            pane.Visibility = Visibility.Collapsed;
            pane.Margin = new Thickness(5);
        }

        switch (count)
        {
            case 1:
                AddDefinitions(1, 1);
                Place(Pane0, 0, 0);
                break;

            case 2:
                AddDefinitions(1, 2);
                Place(Pane0, 0, 0);
                Place(Pane1, 0, 1);
                break;

            case 3:
                AddDefinitions(2, 2);
                Place(Pane0, 0, 0, rowSpan: 2);
                Place(Pane1, 0, 1);
                Place(Pane2, 1, 1);
                break;

            default:
                AddDefinitions(2, 2);
                Place(Pane0, 0, 0);
                Place(Pane1, 0, 1);
                Place(Pane2, 1, 0);
                Place(Pane3, 1, 1);
                break;
        }

        var active = _activePane is not null && _activePane.Visibility == Visibility.Visible
            ? _activePane
            : _panes.First(p => p.Visibility == Visibility.Visible);

        SetActivePane(active);
        UpdateStatusBar();

        void AddDefinitions(int rows, int columns)
        {
            for (var r = 0; r < rows; r++)
                PaneHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            for (var c = 0; c < columns; c++)
                PaneHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        void Place(FilePane pane, int row, int column, int rowSpan = 1)
        {
            Grid.SetRow(pane, row);
            Grid.SetColumn(pane, column);
            Grid.SetRowSpan(pane, rowSpan);
            pane.Visibility = Visibility.Visible;
        }
    }

    private void SetActivePane(FilePane pane)
    {
        _activePane = pane;
        foreach (var candidate in _panes)
            candidate.IsActive = ReferenceEquals(candidate, pane);

        UpdateStatusBar();
    }

    private void CycleActivePane()
    {
        var visible = _panes.Where(p => p.Visibility == Visibility.Visible).ToList();
        if (visible.Count == 0) return;

        var index = _activePane is null ? -1 : visible.IndexOf(_activePane);
        var next = visible[(index + 1) % visible.Count];
        SetActivePane(next);
        next.FocusList();
    }

    private void ToggleHidden()
    {
        var value = !_settings.ShowHidden;
        _settings.ShowHidden = value;
        foreach (var host in _hosts) host.SetShowHidden(value);
    }

    // ---------- global shortcuts ----------

    private async void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled) return;

        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;

        if (e.Key == Key.F3 || (ctrl && !shift && e.Key == Key.F))
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && _search.IsOpen)
        {
            CloseSearch();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key is >= Key.D1 and <= Key.D4)
        {
            ArrangeLayout(e.Key - Key.D0);
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.Tab)
        {
            CycleActivePane();
            e.Handled = true;
            return;
        }

        if (ctrl && !shift && e.Key == Key.T && _activePane is not null)
        {
            _activePane.NewTab();
            e.Handled = true;
            return;
        }

        if (ctrl && !shift && e.Key == Key.W && _activePane is not null)
        {
            _activePane.CloseActiveTab();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.PageDown && _activePane is not null)
        {
            _activePane.CycleTab(1);
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.PageUp && _activePane is not null)
        {
            _activePane.CycleTab(-1);
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.L && _activePane is not null)
        {
            _activePane.FocusAddress();
            e.Handled = true;
            return;
        }

        if (ctrl && shift && e.Key == Key.N && _activePane is not null)
        {
            e.Handled = true;
            await _activePane.NewFolderAsync();
            return;
        }

        var typing = Keyboard.FocusedElement is TextBoxBase;

        if (e.Key == Key.F5 && _activePane is not null)
        {
            e.Handled = true;
            await _activePane.RefreshPreservingSelectionAsync();
            return;
        }

        if (ctrl && !shift && e.Key == Key.H && !typing)
        {
            ToggleHidden();
            e.Handled = true;
            return;
        }

        if (alt && e.Key == Key.Left && !typing && _activePane?.ViewModel is { } backTarget)
        {
            e.Handled = true;
            await backTarget.GoBackAsync();
            return;
        }

        if (alt && e.Key == Key.Right && !typing && _activePane?.ViewModel is { } forwardTarget)
        {
            e.Handled = true;
            await forwardTarget.GoForwardAsync();
            return;
        }

        if (typing || _activePane is null) return;

        if (ctrl && !shift && e.Key == Key.C)
        {
            _activePane.CopySelection(ClipboardMode.Copy);
            e.Handled = true;
        }
        else if (ctrl && !shift && e.Key == Key.X)
        {
            _activePane.CopySelection(ClipboardMode.Cut);
            e.Handled = true;
        }
        else if (ctrl && !shift && e.Key == Key.V)
        {
            e.Handled = true;
            await _activePane.PasteAsync();
        }
        else if (e.Key == Key.Delete)
        {
            e.Handled = true;
            await _activePane.DeleteSelectedAsync();
        }
        else if (e.Key == Key.F2)
        {
            e.Handled = true;
            await _activePane.RenameSelectedAsync();
        }
    }

    // ---------- search ----------

    private async void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                if (SearchList.Items.Count == 0)
                {
                    await _search.SearchNowAsync();
                    break;
                }

                if (ctrl) await RevealFirstResultAsync();
                else OpenResult(SearchList.SelectedItem as FileItem ?? _search.Results[0]);
                break;

            case Key.Escape:
                e.Handled = true;
                CloseSearch();
                break;

            case Key.Down:
                if (_search.Results.Count > 0)
                {
                    e.Handled = true;
                    SearchList.Focus();
                    SearchList.SelectedIndex = 0;
                }
                break;
        }
    }

    private void OnClearSearch(object sender, RoutedEventArgs e) => CloseSearch();

    private void CloseSearch()
    {
        _search.Close();
        SearchBox.Focus();
    }

    private void OnSearchResultDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(SearchList, source) is null) return;
        OpenResult(SearchList.SelectedItem as FileItem);
    }

    private void OpenResult(FileItem? item)
    {
        if (item is null) return;

        if (item.IsDirectory)
        {
            if (_activePane is not null) _ = RevealInActivePaneAsync(item);
        }
        else
        {
            ShellOps.Open(item.FullPath);
        }
    }

    private async Task RevealFirstResultAsync()
    {
        if (_search.Results.Count == 0) return;
        await RevealInActivePaneAsync(SearchList.SelectedItem as FileItem ?? _search.Results[0]);
    }

    private async Task RevealInActivePaneAsync(FileItem item)
    {
        if (_activePane is null) return;
        await _activePane.RevealAsync(item.FullPath);
        CloseSearch();
    }

    private async void OnRevealSearchResult(object sender, RoutedEventArgs e)
    {
        if (SearchList.SelectedItem is FileItem item) await RevealInActivePaneAsync(item);
        else await RevealFirstResultAsync();
    }

    private void OnOpenSearchResult(object sender, RoutedEventArgs e)
    {
        if (SearchList.SelectedItem is not FileItem item) return;
        if (item.IsDirectory) _ = RevealInActivePaneAsync(item);
        else ShellOps.Open(item.FullPath);
    }

    private void OnSearchListKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (ctrl) _ = RevealFirstResultAsync();
            else if (SearchList.SelectedItem is FileItem item) OpenResult(item);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseSearch();
        }
    }

    private void OnSearchResultRightClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(SearchList, (DependencyObject)e.OriginalSource) is not ListViewItem container) return;
        if (!container.IsSelected) container.IsSelected = true;
    }

    private void OnCopySearchPath(object sender, RoutedEventArgs e)
    {
        var paths = SearchList.SelectedItems.Cast<FileItem>().Select(i => i.FullPath).ToList();
        if (paths.Count > 0) Clipboard.SetText(string.Join(Environment.NewLine, paths));
    }

    private void OnSearchResultProperties(object sender, RoutedEventArgs e)
    {
        if (SearchList.SelectedItem is FileItem item) ShellOps.ShowProperties(item.FullPath);
    }

    // ---------- indexing / provider ----------

    private async void OnReindex(object sender, RoutedEventArgs e) => await StartIndexingAsync();

    private void OnCancelIndex(object sender, RoutedEventArgs e)
    {
        _indexCts?.Cancel();
        CancelIndexButton.Visibility = Visibility.Collapsed;
        IndexStatusText.Text = "인덱싱 취소 중…";
    }

    private async Task StartIndexingAsync()
    {
        if (_indexing) return;

        _indexing = true;
        _indexCts = new CancellationTokenSource();
        CancelIndexButton.Visibility = Visibility.Visible;
        IndexStatusText.Text = "인덱싱 시작…";

        var roots = _settings.SearchRoots.Count > 0 ? _settings.SearchRoots : SettingsStore.DefaultRoots();
        var succeeded = await _searchService.Index.BuildAsync(roots, _indexCts.Token);

        _indexing = false;
        CancelIndexButton.Visibility = Visibility.Collapsed;
        if (!succeeded)
            IndexStatusText.Text = "인덱스가 취소되었거나 실패했습니다.";
        else
            IndexStatusText.Text = $"내장 인덱스 {_searchService.Index.Count:N0}개";

        UpdateProviderChip();
        UpdateStatusBar();
    }

    private void UpdateProviderChip()
    {
        if (_searchService.EverythingAvailable)
        {
            ProviderChip.Text = "Everything 연결됨";
            ProviderChip.ToolTip = "Everything SDK(Everything64.dll)로 즉시 검색합니다.";
        }
        else
        {
            ProviderChip.Text = "내장 인덱스";
            ProviderChip.ToolTip = _searchService.Everything.UnavailableReason
                ?? "Everything을 찾지 못해 내장 인덱스로 검색합니다.";
        }
    }

    private void OnToggleTheme(object sender, RoutedEventArgs e)
    {
        _settings.DarkTheme = !ThemeManager.IsDark;
        ThemeManager.Apply(_settings.DarkTheme);
        ThemeManager.ApplyWindowChrome(this, _settings.DarkTheme);
        ThemeButton.Content = ThemeManager.IsDark ? "\uE706" : "\uE708";
    }

    private void OnAppMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = AppMenuButton, Placement = PlacementMode.Bottom };

        var reindex = new MenuItem { Header = "내장 인덱스 다시 만들기" };
        reindex.Click += async (_, _) => await StartIndexingAsync();
        menu.Items.Add(reindex);

        if (_indexing)
        {
            var stop = new MenuItem { Header = "인덱싱 중지" };
            stop.Click += (_, _) => _indexCts?.Cancel();
            menu.Items.Add(stop);
        }

        menu.Items.Add(new Separator());

        var detect = new MenuItem { Header = "Everything 다시 감지" };
        detect.Click += (_, _) =>
        {
            _searchService.RefreshProvider();
            UpdateProviderChip();
            IndexStatusText.Text = _searchService.EverythingAvailable
                ? "Everything 검색 사용 중"
                : "내장 인덱스 사용 중";
        };
        menu.Items.Add(detect);

        var download = new MenuItem { Header = "Everything 다운로드 페이지" };
        download.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("https://www.voidtools.com/downloads/") { UseShellExecute = true }); }
            catch { /* ignore */ }
        };
        menu.Items.Add(download);

        menu.Items.Add(new Separator());

        var settingsFolder = new MenuItem { Header = "설정 폴더 열기" };
        settingsFolder.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(SettingsStore.DataDirectory) { UseShellExecute = true }); }
            catch { /* ignore */ }
        };
        menu.Items.Add(settingsFolder);

        var about = new MenuItem { Header = "정보" };
        about.Click += (_, _) => MessageBox.Show(
            "C-Dir 1.0\n\nQ-Dir 스타일 다중 창 파일 관리자와 Everything 스타일 즉시 검색을 하나로 합친 프로그램입니다.\n\n" +
            "• 1·2·3·4 분할 탐색 (Ctrl+1~4)\n" +
            "• 창마다 여러 탭 (Ctrl+T 새 탭, Ctrl+W 닫기, Ctrl+PgUp/PgDn 전환)\n" +
            "• 주소 표시줄 경로 클릭으로 상위 폴더 이동, 폴더 트리 표시\n" +
            "• 별(☆) 메뉴 또는 폴더 우클릭으로 즐겨찾기 추가·제거\n" +
            "• F3 즉시 검색: Everything SDK가 있으면 Everything, 없으면 내장 인덱스\n" +
            "• Ctrl+C/X/V, F2, Delete, Ctrl+Shift+N 등 탐색기 단축키\n\n" +
            $"검색 공급자: {_searchService.ProviderName}\n" +
            $"내장 인덱스: {_searchService.Index.Count:N0}개\n" +
            $"설정: {SettingsStore.DataDirectory}",
            "C-Dir 정보", MessageBoxButton.OK, MessageBoxImage.Information);
        menu.Items.Add(about);

        menu.IsOpen = true;
    }

    // ---------- status ----------

    private void UpdateStatusBar()
    {
        if (_activePane?.ViewModel is not { } viewModel)
        {
            StatusPath.Text = "";
            StatusSelection.Text = "";
            return;
        }

        StatusPath.Text = string.IsNullOrEmpty(viewModel.CurrentPath) ? "불러오는 중…" : viewModel.CurrentPath;
        StatusPath.ToolTip = StatusPath.Text;

        var selected = _activePane.SelectedItems().Count;
        StatusSelection.Text = selected > 0
            ? $"   ·   {selected:N0}개 선택"
            : $"   ·   {viewModel.View.Count:N0}개 항목";
    }

    // ---------- shutdown ----------

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _indexCts?.Cancel();

        var bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;

        _settings.WindowWidth = bounds.Width;
        _settings.WindowHeight = bounds.Height;
        _settings.WindowLeft = bounds.Left;
        _settings.WindowTop = bounds.Top;
        _settings.Maximized = WindowState == WindowState.Maximized;
        _settings.ShowHidden = _hosts[0].Active?.ShowHidden ?? _settings.ShowHidden;
        _settings.PanePaths = _hosts.Select(h => h.Active?.CurrentPath ?? "").ToList();
        _settings.PaneTabs = _hosts
            .Select(h => h.Tabs
                .Select(t => t.ViewModel.CurrentPath)
                .Where(p => !string.IsNullOrEmpty(p))
                .Take(12)
                .ToList())
            .ToList();
        _settings.PaneActiveTab = _hosts
            .Select(h => h.ActiveTab is null ? 0 : h.Tabs.IndexOf(h.ActiveTab))
            .ToList();
        _settings.Favorites = FavoritesStore.Items.ToList();

        SettingsStore.Save(_settings);
    }
}
