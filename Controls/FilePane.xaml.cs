using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CDir.Dialogs;
using CDir.Models;
using CDir.Services;
using CDir.ViewModels;

namespace CDir.Controls;

public partial class FilePane : UserControl
{
    private PaneHostViewModel? _host;
    private bool _subscribed;
    private bool _switchingTabs;
    private bool _syncingTree;
    private bool _suppressTreeNavigation;
    private bool _treeVisible;
    private bool _treeInitialized;
    private string? _pendingTreeSync;
    private readonly Dictionary<TreeViewItem, Task> _treeLoads = [];

    public FilePane()
    {
        InitializeComponent();

        PreviewMouseDown += (_, _) => Activated?.Invoke(this, EventArgs.Empty);
        GotKeyboardFocus += (_, _) => Activated?.Invoke(this, EventArgs.Empty);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        FileList.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnHeaderClick));

        FileList.AllowDrop = true;
        FileList.PreviewMouseLeftButtonDown += OnListDragStart;
        FileList.PreviewMouseLeftButtonUp += OnListDragEnd;
        FileList.PreviewMouseMove += OnListDragMove;
        FileList.DragOver += OnListDragOver;
        FileList.Drop += OnListDrop;
    }

    private Point _dragOrigin;
    private bool _dragArmed;
    private FileItem? _dragItem;

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(FilePane), new PropertyMetadata(false));

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public FilePaneViewModel? ViewModel => _host?.Active;
    public string CurrentPath => _host?.Active?.CurrentPath ?? "";
    public PaneHostViewModel? Host => _host;

    public event EventHandler? Activated;
    public event EventHandler? PaneStatusChanged;
    public event EventHandler<string>? PanePathChanged;

    public void Attach(PaneHostViewModel host)
    {
        if (_host is not null)
        {
            _host.ActiveChanged -= OnHostActiveChanged;
            _host.PathChanged -= OnHostPathChanged;
        }

        _host = host;
        DataContext = host;
        host.ActiveChanged += OnHostActiveChanged;
        host.PathChanged += OnHostPathChanged;
        OnHostActiveChanged();
    }

    // ---------- tab strip ----------

    public void NewTab()
    {
        if (_host is null) return;

        var path = _host.Active?.CurrentPath;
        if (string.IsNullOrWhiteSpace(path))
            path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        _host.AddTab(path);
    }

    public void CloseActiveTab()
    {
        if (_host?.ActiveTab is { } tab) _host.Close(tab);
    }

    public void CycleTab(int delta) => _host?.Cycle(delta);

    private void OnNewTab(object sender, RoutedEventArgs e) => NewTab();

    private void OnTabClose(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { Tag: FileTab tab } && _host is not null)
            _host.Close(tab);

        FileList.Focus();
    }

    private void OnTabMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null) return;
        if (sender is not FrameworkElement { Tag: FileTab tab } || _host is null) return;

        _host.Activate(tab);
        FileList.Focus();
    }

    private void OnHostActiveChanged()
    {
        _switchingTabs = true;
        try
        {
            FileList.ItemsSource = _host?.Active?.View;
            FileList.SelectedItems.Clear();

            if (_host?.ActiveTab is { } tab && tab.SelectedPaths.Count > 0)
            {
                foreach (var item in tab.ViewModel.Items)
                {
                    if (tab.SelectedPaths.Contains(item.FullPath))
                        FileList.SelectedItems.Add(item);
                }
            }

            HideAddressEditor();
            UpdateHeaderArrows();
        }
        finally
        {
            _switchingTabs = false;
        }

        UpdateFavoriteIcon();

        if (_treeVisible && _host?.Active is { } active && !string.IsNullOrEmpty(active.CurrentPath))
            _ = SyncTreeAsync(active.CurrentPath);

        Dispatcher.BeginInvoke(new Action(() =>
        {
            BringActiveTabIntoView();
            BreadcrumbScroller.ScrollToRightEnd();
        }), DispatcherPriority.Background);

        RaiseStatus();
    }

    private void BringActiveTabIntoView()
    {
        if (_host?.ActiveTab is not { } tab) return;

        TabStrip.UpdateLayout();

        if (TabStrip.ItemContainerGenerator.ContainerFromItem(tab) is FrameworkElement container)
            container.BringIntoView();
        else
            TabScroll.ScrollToRightEnd();
    }

    private void OnHostPathChanged(string path)
    {
        PanePathChanged?.Invoke(this, path);
        Dispatcher.BeginInvoke(new Action(() => BreadcrumbScroller.ScrollToRightEnd()), DispatcherPriority.Background);

        if (_treeVisible) _ = SyncTreeAsync(path);
        UpdateHeaderArrows();
        UpdateFavoriteIcon();
    }

    private void UpdateFavoriteIcon()
    {
        var isFavorite = FavoritesStore.Contains(_host?.Active?.CurrentPath);
        QuickButton.Content = isFavorite ? "\uE735" : "\uE734";
        QuickButton.ToolTip = isFavorite
            ? "즐겨찾기 · 현재 폴더가 즐겨찾기에 있음"
            : "즐겨찾기 · 빠른 이동";
    }

    public void FocusList() => FileList.Focus();

    public void FocusAddress() => ShowAddressEditor(selectAll: true);

    public List<FileItem> SelectedItems() => FileList.SelectedItems.Cast<FileItem>().ToList();

    public void SelectPath(string fullPath)
    {
        if (_host?.Active is not { } vm) return;
        var item = vm.FindItem(fullPath);
        if (item is null) return;

        FileList.SelectedItem = item;
        FileList.ScrollIntoView(item);
        FileList.Focus();
    }

    /// <summary>Navigates to the folder containing <paramref name="fullPath"/> and selects it.</summary>
    public async Task RevealAsync(string fullPath)
    {
        if (_host?.Active is not { } vm) return;

        var directory = Directory.Exists(fullPath) ? fullPath : Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory)) return;

        if (!string.Equals(directory, vm.CurrentPath, StringComparison.OrdinalIgnoreCase))
            await vm.NavigateAsync(directory);
        else
            await vm.RefreshAsync();

        SelectPath(fullPath);
    }

    public async Task RefreshPreservingSelectionAsync()
    {
        if (_host?.Active is not { } vm) return;

        var selected = SelectedItems().Select(i => i.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        await vm.RefreshAsync();
        if (selected.Count == 0) return;

        FileList.SelectedItems.Clear();
        foreach (var item in vm.Items)
        {
            if (selected.Contains(item.FullPath))
                FileList.SelectedItems.Add(item);
        }

        RaiseStatus();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_subscribed) return;
        ShellOps.FileSystemChanged += OnFileSystemChanged;
        FavoritesStore.Changed += OnFavoritesChanged;
        _subscribed = true;
        UpdateFavoriteIcon();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed) return;
        ShellOps.FileSystemChanged -= OnFileSystemChanged;
        FavoritesStore.Changed -= OnFavoritesChanged;
        _subscribed = false;
    }

    private void OnFavoritesChanged() => UpdateFavoriteIcon();

    private void OnFileSystemChanged()
    {
        if (!IsVisible) return;
        Dispatcher.BeginInvoke(new Action(async () => await RefreshPreservingSelectionAsync()));
    }

    // ---------- navigation ----------

    private async void OnBack(object sender, RoutedEventArgs e)
    {
        if (_host?.Active is { } vm) await vm.GoBackAsync();
        FocusList();
    }

    private async void OnForward(object sender, RoutedEventArgs e)
    {
        if (_host?.Active is { } vm) await vm.GoForwardAsync();
        FocusList();
    }

    private async void OnUp(object sender, RoutedEventArgs e)
    {
        if (_host?.Active is { } vm) await vm.GoUpAsync();
        FocusList();
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await RefreshPreservingSelectionAsync();

    private async void OnBreadcrumbClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string path } || _host?.Active is not { } vm) return;

        if (string.Equals(path, vm.CurrentPath, StringComparison.OrdinalIgnoreCase))
        {
            await vm.RefreshAsync();
            return;
        }

        await vm.NavigateAsync(path);
        FileList.Focus();
    }

    private void OnBreadcrumbBackgroundClick(object sender, MouseButtonEventArgs e) => ShowAddressEditor(selectAll: false);

    private void OnEditAddress(object sender, RoutedEventArgs e) => ShowAddressEditor(selectAll: true);

    private void ShowAddressEditor(bool selectAll)
    {
        if (_host?.Active is not { } vm) return;

        AddressBox.Text = vm.CurrentPath;
        BreadcrumbScroller.Visibility = Visibility.Collapsed;
        EditAddressButton.Visibility = Visibility.Collapsed;
        AddressBox.Visibility = Visibility.Visible;
        AddressBox.Focus();

        if (selectAll) AddressBox.SelectAll();
        else AddressBox.CaretIndex = AddressBox.Text.Length;
    }

    private void HideAddressEditor()
    {
        if (AddressBox.Visibility != Visibility.Visible) return;

        AddressBox.Visibility = Visibility.Collapsed;
        EditAddressButton.Visibility = Visibility.Visible;
        BreadcrumbScroller.Visibility = Visibility.Visible;

        if (_host?.Active is { } vm) vm.AddressText = vm.CurrentPath;
    }

    private async void OnAddressKeyDown(object sender, KeyEventArgs e)
    {
        if (_host?.Active is not { } vm) return;

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (await vm.NavigateAsync(AddressBox.Text))
            {
                HideAddressEditor();
                FocusList();
            }
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            HideAddressEditor();
            FocusList();
        }
    }

    private void OnAddressLostFocus(object sender, KeyboardFocusChangedEventArgs e) => HideAddressEditor();

    private void OnQuickMenu(object sender, RoutedEventArgs e)
    {
        if (_host?.Active is not { } vm) return;

        var menu = new ContextMenu { PlacementTarget = QuickButton, Placement = PlacementMode.Bottom };
        var currentPath = vm.CurrentPath;
        var canFavorite = !string.IsNullOrWhiteSpace(currentPath) && Directory.Exists(currentPath);

        var toggle = new MenuItem
        {
            Header = FavoritesStore.Contains(currentPath) ? "현재 폴더를 즐겨찾기에서 제거" : "현재 폴더를 즐겨찾기에 추가",
            IsEnabled = canFavorite,
        };
        toggle.Click += (_, _) => FavoritesStore.Toggle(currentPath);
        menu.Items.Add(toggle);

        if (FavoritesStore.Items.Count > 0)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = "즐겨찾기", IsEnabled = false });

            foreach (var path in FavoritesStore.Items.ToList())
            {
                var favorite = new MenuItem
                {
                    Header = FavoritesStore.DisplayName(path),
                    ToolTip = path,
                };
                favorite.Click += async (_, _) => await vm.NavigateAsync(path);
                menu.Items.Add(favorite);
            }

            var manage = new MenuItem { Header = "즐겨찾기 관리" };
            foreach (var path in FavoritesStore.Items.ToList())
            {
                var remove = new MenuItem
                {
                    Header = $"제거: {FavoritesStore.DisplayName(path)}",
                    ToolTip = path,
                };
                remove.Click += (_, _) => FavoritesStore.Remove(path);
                manage.Items.Add(remove);
            }

            menu.Items.Add(manage);
        }

        menu.Items.Add(new Separator());

        void Add(string header, string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
            var item = new MenuItem { Header = header };
            item.Click += async (_, _) => await vm.NavigateAsync(path);
            menu.Items.Add(item);
        }

        Add("바탕 화면", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        Add("문서", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        Add("다운로드", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
        Add("사진", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        Add("사용자 폴더", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        Add("Windows", Environment.GetFolderPath(Environment.SpecialFolder.Windows));

        menu.Items.Add(new Separator());

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;
            var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.DriveType.ToString() : drive.VolumeLabel;
            Add($"{drive.Name.TrimEnd('\\')}  ({label})", drive.RootDirectory.FullName);
        }

        menu.IsOpen = true;
    }

    private void OnMoreMenu(object sender, RoutedEventArgs e)
    {
        if (_host?.Active is not { } vm) return;

        var menu = new ContextMenu { PlacementTarget = MoreButton, Placement = PlacementMode.Bottom };

        var newFolder = new MenuItem { Header = "새 폴더" };
        newFolder.Click += async (_, _) => await NewFolderAsync();
        menu.Items.Add(newFolder);

        var newTab = new MenuItem { Header = "새 탭 (Ctrl+T)" };
        newTab.Click += (_, _) => NewTab();
        menu.Items.Add(newTab);

        var closeTab = new MenuItem { Header = "탭 닫기 (Ctrl+W)", IsEnabled = _host.CanCloseTabs };
        closeTab.Click += (_, _) => CloseActiveTab();
        menu.Items.Add(closeTab);

        menu.Items.Add(new Separator());

        var terminal = new MenuItem { Header = "터미널에서 열기" };
        terminal.Click += (_, _) => ShellOps.OpenTerminal(vm.CurrentPath);
        menu.Items.Add(terminal);

        menu.Items.Add(new Separator());

        var hidden = new MenuItem { Header = "숨김 항목 표시", IsCheckable = true, IsChecked = vm.ShowHidden };
        hidden.Click += (_, _) => vm.ShowHidden = hidden.IsChecked;
        menu.Items.Add(hidden);

        var refresh = new MenuItem { Header = "새로 고침 (F5)" };
        refresh.Click += async (_, _) => await RefreshPreservingSelectionAsync();
        menu.Items.Add(refresh);

        var copyPath = new MenuItem { Header = "현재 경로 복사" };
        copyPath.Click += (_, _) => Clipboard.SetText(vm.CurrentPath);
        menu.Items.Add(copyPath);

        var properties = new MenuItem { Header = "폴더 속성" };
        properties.Click += (_, _) => ShellOps.ShowProperties(vm.CurrentPath);
        menu.Items.Add(properties);

        menu.IsOpen = true;
    }

    // ---------- folder tree ----------

    private void OnToggleTree(object sender, RoutedEventArgs e)
    {
        _treeVisible = !_treeVisible;

        TreeColumn.Width = _treeVisible ? new GridLength(220) : new GridLength(0);
        SplitterColumn.Width = _treeVisible ? new GridLength(6) : new GridLength(0);
        TreeHost.Visibility = _treeVisible ? Visibility.Visible : Visibility.Collapsed;
        TreeSplitter.Visibility = _treeVisible ? Visibility.Visible : Visibility.Collapsed;
        TreeButton.Opacity = _treeVisible ? 1 : 0.55;

        if (!_treeVisible) return;

        InitializeTree();
        if (_host?.Active is { } vm && !string.IsNullOrEmpty(vm.CurrentPath))
            _ = SyncTreeAsync(vm.CurrentPath);
    }

    private void InitializeTree()
    {
        if (_treeInitialized) return;
        _treeInitialized = true;

        FolderTree.Items.Clear();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;
            var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.DriveType.ToString() : drive.VolumeLabel;
            FolderTree.Items.Add(CreateTreeNode(drive.RootDirectory.FullName, $"{drive.Name.TrimEnd('\\')}  ({label})"));
        }
    }

    private TreeViewItem CreateTreeNode(string path, string header)
    {
        var node = new TreeViewItem
        {
            Header = header,
            Tag = path,
            Uid = "unloaded",
            Style = (Style)FindResource("FolderTreeItem"),
        };
        node.Items.Add(new TreeViewItem());
        node.Expanded += OnTreeExpanded;
        return node;
    }

    private async void OnTreeExpanded(object sender, RoutedEventArgs e)
    {
        if (sender is TreeViewItem node) await EnsureChildrenAsync(node);
    }

    private async Task EnsureChildrenAsync(TreeViewItem node)
    {
        if (node.Uid == "loaded" || node.Tag is not string path) return;

        if (_treeLoads.TryGetValue(node, out var existing))
        {
            await existing;
            return;
        }

        node.Uid = "loading";
        var task = LoadChildrenAsync(node, path);
        _treeLoads[node] = task;
        await task;
    }

    private async Task LoadChildrenAsync(TreeViewItem node, string path)
    {
        try
        {
            var children = await Task.Run(() => GetSubDirectories(path));

            node.Items.Clear();
            foreach (var child in children)
                node.Items.Add(CreateTreeNode(child, Path.GetFileName(child)));

            node.Uid = "loaded";
        }
        finally
        {
            _treeLoads.Remove(node);
        }
    }

    private async void OnTreeSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_suppressTreeNavigation || _syncingTree) return;
        if (e.NewValue is not TreeViewItem { Tag: string path } || _host?.Active is not { } vm) return;
        if (string.Equals(vm.CurrentPath, path, StringComparison.OrdinalIgnoreCase)) return;

        await vm.NavigateAsync(path);
    }

    private async Task SyncTreeAsync(string path)
    {
        if (!_treeVisible || string.IsNullOrEmpty(path)) return;

        if (_syncingTree)
        {
            _pendingTreeSync = path;
            return;
        }

        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root)) return;

        var rootNode = FolderTree.Items.OfType<TreeViewItem>()
            .FirstOrDefault(n => string.Equals(n.Tag as string, root, StringComparison.OrdinalIgnoreCase));
        if (rootNode is null) return;

        _syncingTree = true;
        try
        {
            var node = rootNode;
            node.IsExpanded = true;
            await EnsureChildrenAsync(node);

            var remainder = path[root.Length..].Trim(Path.DirectorySeparatorChar);
            if (remainder.Length > 0)
            {
                var parts = remainder.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
                for (var i = 0; i < parts.Length; i++)
                {
                    var currentPath = ((string)node.Tag!).TrimEnd(Path.DirectorySeparatorChar)
                                      + Path.DirectorySeparatorChar + parts[i];
                    var child = node.Items.OfType<TreeViewItem>()
                        .FirstOrDefault(n => string.Equals(n.Tag as string, currentPath, StringComparison.OrdinalIgnoreCase));
                    if (child is null) break;

                    node = child;
                    if (i == parts.Length - 1) break;

                    node.IsExpanded = true;
                    await EnsureChildrenAsync(node);
                }
            }

            _suppressTreeNavigation = true;
            node.IsSelected = true;
            node.BringIntoView();
        }
        finally
        {
            _suppressTreeNavigation = false;
            _syncingTree = false;

            var pending = _pendingTreeSync;
            _pendingTreeSync = null;
            if (pending is not null && !string.Equals(pending, path, StringComparison.OrdinalIgnoreCase))
                _ = SyncTreeAsync(pending);
        }
    }

    private static List<string> GetSubDirectories(string path)
    {
        try
        {
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
                RecurseSubdirectories = false,
                ReturnSpecialDirectories = false,
            };

            var directories = new DirectoryInfo(path).EnumerateDirectories("*", options).ToList();
            directories.Sort((a, b) => NaturalComparer.Compare(a.Name, b.Name));
            return directories.Select(d => d.FullName).ToList();
        }
        catch
        {
            return [];
        }
    }

    // ---------- sorting ----------

    private void OnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader { Column: { } column } || _host?.Active is not { } vm) return;

        var key = ColumnKey(column.Header as string);
        if (key is null) return;

        vm.SortBy(key);
        UpdateHeaderArrows();
        e.Handled = true;
    }

    private void UpdateHeaderArrows()
    {
        if (_host?.Active is not { } vm || FileList.View is not GridView grid) return;

        foreach (var column in grid.Columns)
        {
            if (column.Header is not string text) continue;

            var key = ColumnKey(text);
            if (key is null) continue;

            var baseText = text.TrimEnd('▲', '▼', ' ');
            column.Header = key == vm.SortColumn
                ? baseText + (vm.SortAscending ? " ▲" : " ▼")
                : baseText;
        }
    }

    private static string? ColumnKey(string? header) => header switch
    {
        null => null,
        _ when header.StartsWith("이름", StringComparison.Ordinal) => "Name",
        _ when header.StartsWith("크기", StringComparison.Ordinal) => "Size",
        _ when header.StartsWith("수정한 날짜", StringComparison.Ordinal) => "Modified",
        _ when header.StartsWith("유형", StringComparison.Ordinal) => "Type",
        _ => null,
    };

    // ---------- list interaction ----------

    private async void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileList.SelectedItem is not FileItem item) return;
        if (e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(FileList, source) is null) return;

        await OpenItemAsync(item);
    }

    private async Task OpenItemAsync(FileItem item)
    {
        if (item.IsDirectory)
        {
            if (_host?.Active is { } vm && await vm.NavigateAsync(item.FullPath))
                FocusList();
        }
        else
        {
            ShellOps.Open(item.FullPath);
        }
    }

    private async void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (_host?.Active is not { } vm) return;

        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                await OpenSelectionAsync();
                break;

            case Key.Back:
                e.Handled = true;
                await vm.GoUpAsync();
                break;

            case Key.Delete:
                e.Handled = true;
                await DeleteSelectedAsync();
                break;

            case Key.F2:
                e.Handled = true;
                await RenameSelectedAsync();
                break;

            case Key.F5:
                e.Handled = true;
                await RefreshPreservingSelectionAsync();
                break;

            case Key.A when Keyboard.Modifiers == ModifierKeys.Control:
                e.Handled = true;
                FileList.SelectAll();
                break;
        }
    }

    private async Task OpenSelectionAsync()
    {
        var items = SelectedItems();
        if (items.Count == 0) return;

        var directory = items.FirstOrDefault(i => i.IsDirectory);
        if (directory is not null)
        {
            await OpenItemAsync(directory);
            return;
        }

        foreach (var file in items) ShellOps.Open(file.FullPath);
    }

    private void OnOpenInNewTab(object sender, RoutedEventArgs e)
    {
        if (_host is null) return;

        var item = FileList.SelectedItem as FileItem;
        var path = item is null
            ? _host.Active?.CurrentPath
            : item.IsDirectory ? item.FullPath : item.DirectoryPath;

        if (string.IsNullOrWhiteSpace(path)) return;
        _host.AddTab(path);
    }

    private void OnListRightClick(object sender, MouseButtonEventArgs e)
    {
        DisarmDrag();

        if (ItemsControl.ContainerFromElement(FileList, (DependencyObject)e.OriginalSource) is not ListViewItem container) return;

        if (!container.IsSelected)
        {
            FileList.SelectedItems.Clear();
            container.IsSelected = true;
        }
    }

    // ---------- drag & drop ----------

    private void OnListDragStart(object sender, MouseButtonEventArgs e)
    {
        var container = ItemsControl.ContainerFromElement(FileList, (DependencyObject)e.OriginalSource) as ListViewItem;
        _dragItem = container?.Content as FileItem;
        _dragArmed = _dragItem is not null;
        _dragOrigin = e.GetPosition(FileList);
    }

    private void OnListDragEnd(object sender, MouseButtonEventArgs e) => DisarmDrag();

    private void DisarmDrag()
    {
        _dragArmed = false;
        _dragItem = null;
    }

    private void OnListDragMove(object sender, MouseEventArgs e)
    {
        if (!_dragArmed || _dragItem is null || e.LeftButton != MouseButtonState.Pressed) return;

        // A context menu can swallow the mouse-down; never drag a stale selection.
        if (!FileList.SelectedItems.Contains(_dragItem))
        {
            DisarmDrag();
            return;
        }

        var position = e.GetPosition(FileList);
        if (Math.Abs(position.X - _dragOrigin.X) < 6 && Math.Abs(position.Y - _dragOrigin.Y) < 6) return;

        var items = SelectedItems();
        if (items.Count == 0) return;

        DisarmDrag();
        var data = new DataObject(DataFormats.FileDrop, items.Select(i => i.FullPath).ToArray());
        DragDrop.DoDragDrop(FileList, data, DragDropEffects.Copy | DragDropEffects.Move);
    }

    private void OnListDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;

        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || DropDirectory(e) is null)
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        e.Effects = (Keyboard.Modifiers & ModifierKeys.Control) != 0
            ? DragDropEffects.Copy
            : DragDropEffects.Move;
    }

    private void OnListDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] sources || sources.Length == 0) return;

        var target = DropDirectory(e);
        if (target is null) return;

        sources = sources
            .Where(p => !string.Equals(p, target, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (sources.Length == 0) return;

        var copy = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var succeeded = copy ? ShellOps.Copy(sources, target) : ShellOps.Move(sources, target);

        if (!copy && succeeded) FileClipboard.Clear();
        if (succeeded) _ = RefreshPreservingSelectionAsync();
    }

    private string? DropDirectory(DragEventArgs e)
    {
        var hit = FileList.InputHitTest(e.GetPosition(FileList)) as DependencyObject;
        if (hit is not null &&
            ItemsControl.ContainerFromElement(FileList, hit) is ListViewItem { Content: FileItem { IsDirectory: true } folder })
            return folder.FullPath;

        return _host?.Active?.CurrentPath;
    }

    // ---------- context menu ----------

    private void OnFileContextMenuOpened(object sender, RoutedEventArgs e)
    {
        DisarmDrag();

        var path = FavoriteTargetPath();
        FavoriteMenuItem.Tag = path;

        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            FavoriteMenuItem.Visibility = Visibility.Collapsed;
            return;
        }

        FavoriteMenuItem.Visibility = Visibility.Visible;
        FavoriteMenuItem.Header = FavoritesStore.Contains(path) ? "즐겨찾기에서 제거" : "즐겨찾기에 추가";
    }

    private string? FavoriteTargetPath()
    {
        if (FileList.SelectedItems.Count == 0)
            return _host?.Active?.CurrentPath;

        if (FileList.SelectedItems.Count != 1)
            return null;

        return FileList.SelectedItem is FileItem { IsDirectory: true } folder
            ? folder.FullPath
            : null;
    }

    private void OnToggleFavorite(object sender, RoutedEventArgs e)
    {
        if (FavoriteMenuItem.Tag is not string path) return;
        FavoritesStore.Toggle(path);
    }

    private async void OnOpen(object sender, RoutedEventArgs e) => await OpenSelectionAsync();

    private void OnReveal(object sender, RoutedEventArgs e)
    {
        var item = FileList.SelectedItem as FileItem;
        if (item is not null) ShellOps.RevealInExplorer(item.FullPath);
    }

    private void OnTerminal(object sender, RoutedEventArgs e) => ShellOps.OpenTerminal(_host?.Active?.CurrentPath ?? "");

    private void OnCopy(object sender, RoutedEventArgs e) => CopySelection(ClipboardMode.Copy);
    private void OnCut(object sender, RoutedEventArgs e) => CopySelection(ClipboardMode.Cut);

    public void CopySelection(ClipboardMode mode)
    {
        var items = SelectedItems();
        if (items.Count == 0) return;
        FileClipboard.Set(items.Select(i => i.FullPath), mode);
    }

    private async void OnPaste(object sender, RoutedEventArgs e) => await PasteAsync();

    public async Task PasteAsync()
    {
        if (_host?.Active is not { } vm) return;

        var sources = FileClipboard.GetPastePaths();
        if (sources.Count == 0) return;

        var target = FileList.SelectedItems.Count == 1 && FileList.SelectedItem is FileItem { IsDirectory: true } folder
            ? folder.FullPath
            : vm.CurrentPath;

        var mode = FileClipboard.Mode;
        var succeeded = mode == ClipboardMode.Cut
            ? ShellOps.Move(sources, target)
            : ShellOps.Copy(sources, target);

        if (!succeeded) return;
        if (mode == ClipboardMode.Cut) FileClipboard.Clear();

        await RefreshPreservingSelectionAsync();
    }

    private async void OnRename(object sender, RoutedEventArgs e) => await RenameSelectedAsync();

    public async Task RenameSelectedAsync()
    {
        if (_host?.Active is not { } vm) return;

        var item = FileList.SelectedItems.Count == 1 ? FileList.SelectedItem as FileItem : null;
        if (item is null) return;

        var name = InputDialog.Show(Window.GetWindow(this), "이름 바꾸기", "새 이름:", item.Name);
        if (string.IsNullOrWhiteSpace(name) || name == item.Name) return;
        if (!IsValidFileName(name))
        {
            MessageBox.Show("파일 이름에 사용할 수 없는 문자가 있습니다.", "C-Dir", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            // For directories DirectoryPath is the folder itself, not its parent,
            // so always rename inside the actual parent directory.
            var parent = Path.GetDirectoryName(item.FullPath);
            if (string.IsNullOrEmpty(parent))
            {
                MessageBox.Show("드라이브 루트 폴더의 이름은 바꿀 수 없습니다.", "C-Dir", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var target = Path.Combine(parent, name);
            if (Directory.Exists(target) || File.Exists(target))
            {
                MessageBox.Show("같은 이름의 항목이 이미 있습니다.", "C-Dir", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (item.IsDirectory) Directory.Move(item.FullPath, target);
            else File.Move(item.FullPath, target);

            await vm.RefreshAsync();
            SelectPath(target);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"이름을 바꿀 수 없습니다.\n{ex.Message}", "C-Dir", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnDelete(object sender, RoutedEventArgs e) => await DeleteSelectedAsync();

    public async Task DeleteSelectedAsync()
    {
        if (_host?.Active is null) return;

        var items = SelectedItems();
        if (items.Count == 0) return;

        if (!ShellOps.DeleteToRecycleBin(items.Select(i => i.FullPath).ToList())) return;
        await RefreshPreservingSelectionAsync();
    }

    private async void OnNewFolder(object sender, RoutedEventArgs e) => await NewFolderAsync();

    public async Task NewFolderAsync()
    {
        if (_host?.Active is not { } vm) return;

        var name = InputDialog.Show(Window.GetWindow(this), "새 폴더", "폴더 이름:", "새 폴더");
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!IsValidFileName(name))
        {
            MessageBox.Show("폴더 이름에 사용할 수 없는 문자가 있습니다.", "C-Dir", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var path = Path.Combine(vm.CurrentPath, name);
            if (Directory.Exists(path) || File.Exists(path))
            {
                MessageBox.Show("같은 이름의 항목이 이미 있습니다.", "C-Dir", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Directory.CreateDirectory(path);
            await vm.RefreshAsync();
            SelectPath(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"폴더를 만들 수 없습니다.\n{ex.Message}", "C-Dir", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnCopyPath(object sender, RoutedEventArgs e)
    {
        var paths = SelectedItems().Select(i => i.FullPath).ToList();
        if (paths.Count > 0) Clipboard.SetText(string.Join(Environment.NewLine, paths));
    }

    private void OnProperties(object sender, RoutedEventArgs e)
    {
        var item = FileList.SelectedItem as FileItem;
        ShellOps.ShowProperties(item?.FullPath ?? _host?.Active?.CurrentPath ?? "");
    }

    // ---------- status ----------

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_switchingTabs && _host?.ActiveTab is { } tab)
        {
            tab.SelectedPaths.Clear();
            foreach (var item in FileList.SelectedItems.Cast<FileItem>())
                tab.SelectedPaths.Add(item.FullPath);
        }

        RaiseStatus();
    }

    private void RaiseStatus() => PaneStatusChanged?.Invoke(this, EventArgs.Empty);

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match) return match;
            source = source is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : null;
        }

        return null;
    }

    private static bool IsValidFileName(string name) =>
        name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && name is not "." and not "..";
}
