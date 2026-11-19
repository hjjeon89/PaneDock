using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PaneDock.Core.Models;
using PaneDock.Core.Services;

namespace PaneDock.App.Controls;

public partial class ExplorerPane : UserControl
{
    private readonly DirectoryListingService _listing = new();
    private readonly List<ExplorerTab> _tabs = [];
    private readonly DispatcherTimer _filterTimer;
    private ExplorerTab? _displayedTab;
    private bool _disposed;
    private bool _changingTab;
    private bool _updatingFilterControls;
    private bool _applyingSnapshot;
    private int _tabCounter;

    public event EventHandler? Activated;
    public event EventHandler? SessionChanged;
    public event Action<ExplorerPane, int>? MoveTabRequested;

    public int Number { get; }

    public ExplorerPane(int number, PaneSession? saved = null)
    {
        InitializeComponent();
        Number = number;
        PaneTitle.Text = $"PANE {number}";
        _filterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _filterTimer.Tick += (_, _) =>
        {
            _filterTimer.Stop();
            if (SelectedTab is { } selected)
            {
                selected.View.Refresh();
                UpdateStatus();
            }
        };
        if (saved?.TabPaths is { Count: > 0 } paths)
        {
            for (var i = 0; i < paths.Count; i++)
                AddTab(paths[i], restoreFallback: true, saved?.TabStates?.ElementAtOrDefault(i));

            _changingTab = true;
            Tabs.SelectedIndex = Math.Clamp(saved?.ActiveTabIndex ?? 0, 0, _tabs.Count - 1);
            _changingTab = false;
            if (SelectedTab is { } selected)
                ShowTab(selected);
        }
        else
        {
            AddTab(ExplorerLocations.ThisPc);
        }
    }

    public PaneSession CaptureSession() => new()
    {
        TabPaths = _tabs.Select(tab => string.IsNullOrWhiteSpace(tab.Path)
            ? tab.RequestedPath : tab.Path).ToList(),
        ActiveTabIndex = Math.Max(0, Tabs.SelectedIndex),
        TabStates = _tabs.Select(CaptureTabState).ToList()
    };

    public void ReceiveTab(string path, TabViewState state) =>
        AddTab(path, restoreFallback: true, state);

    public (string Path, TabViewState State)? GetActiveTabTransfer()
    {
        if (SelectedTab is not { } tab)
            return null;
        return (string.IsNullOrEmpty(tab.Path) ? tab.RequestedPath : tab.Path, CaptureTabState(tab));
    }

    public void FinishTabMove()
    {
        if (SelectedTab is not { } tab)
            return;
        if (_tabs.Count > 1)
            CloseTab(tab);
        else
            _ = NavigateAsync(tab, ExplorerLocations.ThisPc, addHistory: true);
    }

    private static TabViewState CaptureTabState(ExplorerTab tab) => new()
    {
        FilterText = tab.FilterText,
        FilterVisible = tab.FilterVisible,
        ShowHidden = tab.ShowHidden,
        SortColumn = tab.SortColumn,
        SortDescending = tab.SortDescending
    };

    public void SetActive(bool active)
    {
        OuterBorder.BorderBrush = active
            ? (Brush)FindResource("AccentBrush")
            : (Brush)FindResource("BorderBrush");
        OuterBorder.BorderThickness = active ? new Thickness(2) : new Thickness(1);
    }

    private ExplorerTab? SelectedTab => (Tabs.SelectedItem as TabItem)?.Tag as ExplorerTab;

    private void AddTab(string path, bool restoreFallback = false, TabViewState? state = null)
    {
        var tab = new ExplorerTab { RequestedPath = path };
        if (state is not null)
        {
            tab.FilterText = state.FilterText;
            tab.FilterVisible = state.FilterVisible;
            tab.ShowHidden = state.ShowHidden;
            tab.SortColumn = state.SortColumn;
            tab.SortDescending = state.SortDescending;
            ApplyTabSort(tab);
        }
        _tabs.Add(tab);
        var label = new TextBlock { Text = $"탭 {++_tabCounter}", VerticalAlignment = VerticalAlignment.Center };
        var close = new Button
        {
            Content = "×",
            ToolTip = "탭 닫기 (Ctrl+W)",
            Tag = tab,
            Width = 18,
            Height = 18,
            MinHeight = 0,
            Padding = new Thickness(0),
            Margin = new Thickness(7, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0)
        };
        close.Click += CloseTab_Click;
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(label);
        header.Children.Add(close);
        tab.HeaderLabel = label;
        _changingTab = true;
        var tabItem = new TabItem { Header = header, Tag = tab };
        var context = new ContextMenu();
        var duplicate = new MenuItem { Header = "탭 복제" };
        duplicate.Click += (_, _) =>
            AddTab(string.IsNullOrEmpty(tab.Path) ? tab.RequestedPath : tab.Path,
                restoreFallback: true, CaptureTabState(tab));
        context.Items.Add(duplicate);
        var sendTo = new MenuItem { Header = "다른 패널로 이동" };
        for (var i = 1; i <= 4; i++)
        {
            if (i == Number) continue;
            var target = i;
            var to = new MenuItem { Header = $"PANE {target}" };
            to.Click += (_, _) =>
            {
                Tabs.SelectedItem = tabItem;
                MoveTabRequested?.Invoke(this, target);
            };
            sendTo.Items.Add(to);
        }
        context.Items.Add(sendTo);
        tabItem.ContextMenu = context;
        Tabs.Items.Add(tabItem);
        Tabs.SelectedIndex = _tabs.Count - 1;
        _changingTab = false;
        ShowTab(tab);
        _ = NavigateAsync(tab, path, addHistory: true, restoreFallback: restoreFallback);
        Activated?.Invoke(this, EventArgs.Empty);
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { Tag: ExplorerTab tab })
            CloseTab(tab);
    }

    private void CloseTab(ExplorerTab tab)
    {
        if (_tabs.Count <= 1)
            return;
        var index = _tabs.IndexOf(tab);
        if (index < 0)
            return;

        _filterTimer.Stop();
        _changingTab = true;
        if (ReferenceEquals(_displayedTab, tab))
            _displayedTab = null;
        _tabs.RemoveAt(index);
        Tabs.Items.RemoveAt(index);
        Tabs.SelectedIndex = Math.Min(index, _tabs.Count - 1);
        _changingTab = false;
        tab.Dispose();
        if (SelectedTab is { } next)
            ShowTab(next);
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task NavigateAsync(
        ExplorerTab tab, string path, bool addHistory,
        int? historyIndex = null, bool restoreFallback = false)
    {
        if (_disposed || tab.IsDisposed)
            return;
        tab.CancelCurrentLoad();
        var request = tab.Generation;
        var token = tab.Cancellation.Token;
        tab.IsLoading = true;
        tab.IsCheckingPath = true;
        tab.Error = null;
        if (ReferenceEquals(SelectedTab, tab))
            UpdateStatus();

        string fullPath;
        try
        {
            fullPath = ExplorerLocations.IsThisPc(path) || string.Equals(path.Trim(), "내 PC", StringComparison.Ordinal)
                ? ExplorerLocations.ThisPc : Path.GetFullPath(path);
            if (!ExplorerLocations.IsThisPc(fullPath)
                && !await Task.Run(() => Directory.Exists(fullPath), token))
                throw new DirectoryNotFoundException($"폴더를 찾을 수 없습니다: {fullPath}");
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            if (restoreFallback && tab.Generation == request)
            {
                await NavigateAsync(tab, ExplorerLocations.ThisPc, addHistory: true);
                return;
            }
            if (tab.Generation == request)
            {
                tab.Error = ex.Message;
                tab.IsLoading = false;
                tab.IsCheckingPath = false;
                if (ReferenceEquals(SelectedTab, tab))
                    UpdateStatus();
            }
            return;
        }

        if (tab.Generation != request)
            return;

        ConfigureWatcher(tab, fullPath);
        tab.IsCheckingPath = false;
        if (!string.IsNullOrEmpty(tab.Path) && !SamePath(tab.Path, fullPath))
        {
            // The filename filter applies to the folder in which it was typed;
            // navigating to Desktop/another folder should show its contents.
            tab.FilterText = "";
            tab.View.Refresh();
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }
        tab.Items.Clear();
        tab.SelectedEntries.Clear();
        tab.Path = fullPath;
        tab.RequestedPath = fullPath;
        tab.HeaderLabel.Text = ExplorerLocations.IsThisPc(fullPath)
            ? "내 PC"
            : Path.GetFileName(Path.TrimEndingDirectorySeparator(fullPath)) is { Length: > 0 } name
                ? name : fullPath;
        if (addHistory)
        {
            tab.History.RemoveRange(tab.HistoryIndex + 1, tab.History.Count - tab.HistoryIndex - 1);
            tab.History.Add(fullPath);
            tab.HistoryIndex = tab.History.Count - 1;
        }
        else if (historyIndex is not null)
        {
            tab.HistoryIndex = historyIndex.Value;
        }

        if (ReferenceEquals(SelectedTab, tab))
            ShowTab(tab);
        SessionChanged?.Invoke(this, EventArgs.Empty);

        var progress = new Progress<IReadOnlyList<ExplorerEntry>>(batch =>
        {
            if (tab.Generation != request)
                return;
            foreach (var entry in batch)
                tab.Items.Add(entry);
            if (ReferenceEquals(SelectedTab, tab))
                UpdateStatus();
        });

        try
        {
            await _listing.EnumerateAsync(fullPath, progress, token);
        }
        catch (OperationCanceledException)
        {
            // 새 위치 탐색이 시작되면 이전 작업은 무시한다.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (tab.Generation == request)
                tab.Error = $"읽기 실패: {ex.Message}";
        }
        finally
        {
            if (tab.Generation == request)
            {
                tab.IsLoading = false;
                if (ReferenceEquals(SelectedTab, tab))
                    UpdateStatus();
                if (tab.RefreshQueued)
                    RequestSnapshotRefresh(tab);
            }
        }
    }

    private void ShowTab(ExplorerTab tab)
    {
        if (!ReferenceEquals(tab, _displayedTab))
        {
            if (_displayedTab is not null)
            {
                _displayedTab.SelectedEntries.Clear();
                _displayedTab.SelectedEntries.AddRange(Files.SelectedItems.OfType<ExplorerEntry>());
            }
            _filterTimer.Stop();
            Files.ItemsSource = tab.View;
            _displayedTab = tab;
            foreach (var item in tab.SelectedEntries)
            {
                if (tab.View.Contains(item))
                    Files.SelectedItems.Add(item);
            }
        }

        _updatingFilterControls = true;
        Address.Text = ExplorerLocations.IsThisPc(tab.Path) ? "내 PC" : tab.Path;
        EndAddressEdit();
        RefreshBreadcrumb(tab.Path);
        FilterBox.Text = tab.FilterText;
        ShowHidden.IsChecked = tab.ShowHidden;
        SetFilterVisible(tab.FilterVisible);
        _updatingFilterControls = false;
        foreach (var column in Files.Columns)
            column.SortDirection = column.SortMemberPath == tab.SortColumn
                ? tab.SortDescending ? ListSortDirection.Descending : ListSortDirection.Ascending
                : null;
        UpdateStatus();
        _ = SyncFolderTreeAsync();
    }

    private void UpdateStatus()
    {
        if (SelectedTab is not { } tab)
            return;
        if (!string.IsNullOrWhiteSpace(tab.Error))
        {
            Status.Text = tab.Error;
            return;
        }
        var count = Files.Items.Count;
        var selected = Files.SelectedItems.Count;
        Status.Text = $"{count:N0}개 표시 / 전체 {tab.Items.Count:N0}개"
            + (selected > 0 ? $" · {selected:N0}개 선택" : "")
            + (tab.IsLoading ? " · 읽는 중…" : "")
            + (tab.WatchError is not null ? " · 자동 감시 연결 대기 중 (F5로 갱신 가능)" : "");
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changingTab || !ReferenceEquals(e.Source, Tabs) || SelectedTab is not { } tab)
            return;
        ShowTab(tab);
        Activated?.Invoke(this, EventArgs.Empty);
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NewTab_Click(object sender, RoutedEventArgs e) =>
        AddTab(SelectedTab?.Path ?? ExplorerLocations.ThisPc);

    private void SetFilterVisible(bool visible)
    {
        FilterRow.Height = new GridLength(visible ? 30 : 0);
        FilterPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ToggleFilter_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTab is not { } tab)
            return;
        tab.FilterVisible = !tab.FilterVisible;
        SetFilterVisible(tab.FilterVisible);
        if (tab.FilterVisible)
        {
            FilterBox.Focus();
            FilterBox.SelectAll();
        }
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingFilterControls || SelectedTab is not { } tab)
            return;
        tab.FilterText = FilterBox.Text;
        _filterTimer.Stop();
        _filterTimer.Start();
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ShowHidden_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingFilterControls || SelectedTab is not { } tab)
            return;
        tab.ShowHidden = ShowHidden.IsChecked == true;
        tab.View.Refresh();
        UpdateStatus();
        _ = SyncFolderTreeAsync();
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void ApplyTabSort(ExplorerTab tab)
    {
        tab.View.SortDescriptions.Clear();
        if (!string.IsNullOrEmpty(tab.SortColumn))
            tab.View.SortDescriptions.Add(new SortDescription(tab.SortColumn,
                tab.SortDescending ? ListSortDirection.Descending : ListSortDirection.Ascending));
    }

    private void Files_Sorting(object sender, DataGridSortingEventArgs e)
    {
        if (SelectedTab is not { } tab || string.IsNullOrWhiteSpace(e.Column.SortMemberPath))
            return;
        e.Handled = true;
        var property = e.Column.SortMemberPath;
        tab.SortDescending = tab.SortColumn == property && !tab.SortDescending;
        tab.SortColumn = property;
        ApplyTabSort(tab);
        foreach (var column in Files.Columns)
            column.SortDirection = column == e.Column
                ? tab.SortDescending ? ListSortDirection.Descending : ListSortDirection.Ascending
                : null;
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Drives_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTab is { } tab && !ExplorerLocations.IsThisPc(tab.Path))
            _ = NavigateAsync(tab, ExplorerLocations.ThisPc, addHistory: true);
    }

    private void Desktop_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTab is { } tab)
            _ = NavigateAsync(tab,
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                addHistory: true);
    }

    private void Address_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            EndAddressEdit();
            return;
        }
        if (e.Key != Key.Enter || SelectedTab is not { } tab)
            return;
        e.Handled = true;
        _ = NavigateAsync(tab, Address.Text, addHistory: true);
    }

    private void BeginAddressEdit()
    {
        if (SelectedTab is not { } tab)
            return;
        Address.Text = ExplorerLocations.IsThisPc(tab.Path) ? "내 PC" : tab.Path;
        BreadcrumbSurface.Visibility = Visibility.Collapsed;
        Address.Visibility = Visibility.Visible;
        Address.Focus();
        Address.SelectAll();
    }

    private void EndAddressEdit()
    {
        Address.Visibility = Visibility.Collapsed;
        BreadcrumbSurface.Visibility = Visibility.Visible;
    }

    private void EditAddress_Click(object sender, RoutedEventArgs e) => BeginAddressEdit();

    private void BreadcrumbSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2
            && (e.OriginalSource is not DependencyObject origin || FindParent<Button>(origin) is null))
        {
            e.Handled = true;
            BeginAddressEdit();
        }
    }

    private void RefreshBreadcrumb(string path)
    {
        BreadcrumbItems.Children.Clear();
        if (string.IsNullOrWhiteSpace(path))
            return;

        var segments = ExplorerLocations.IsThisPc(path)
            ? [new BreadcrumbSegment("내 PC", ExplorerLocations.ThisPc)]
            : PathBreadcrumb.Build(path).ToArray();

        foreach (var segment in segments)
        {
            if (BreadcrumbItems.Children.Count > 0)
            {
                BreadcrumbItems.Children.Add(new TextBlock
                {
                    Text = "›", FontSize = 14, Margin = new Thickness(1, 0, 1, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Brush)FindResource("TextSecondary")
                });
            }

            var location = segment.FullPath;
            var button = new Button
            {
                // A TextBlock preserves literal underscores (Button.Content otherwise
                // interprets them as keyboard access-key markers).
                Content = new TextBlock
                {
                    Text = segment.Label, TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 160
                },
                Tag = location,
                ToolTip = location,
                MinHeight = 25, Padding = new Thickness(4, 2, 4, 2),
                Margin = new Thickness(0),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            AutomationProperties.SetName(button, segment.Label);
            button.Click += (_, _) =>
            {
                if (SelectedTab is { } selected && !SamePath(selected.Path, location))
                    _ = NavigateAsync(selected, location, addHistory: true);
            };
            BreadcrumbItems.Children.Add(button);
        }

        // Four-pane layouts are narrow: keep the last segments visible while
        // preserving access to ancestors by horizontal scroll.
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (SamePath(SelectedTab?.Path ?? "", path))
                BreadcrumbScroll.ScrollToRightEnd();
        }, DispatcherPriority.Loaded);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTab is { Path.Length: > 0 } tab)
        {
            InvalidateTree([tab.Path]);
            _ = NavigateAsync(tab, tab.Path, addHistory: false);
        }
    }

    private void Up_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTab is not { } tab || ExplorerLocations.IsThisPc(tab.Path))
            return;
        var parent = Directory.GetParent(tab.Path);
        if (parent is not null)
            _ = NavigateAsync(tab, parent.FullName, addHistory: true);
        else
            _ = NavigateAsync(tab, ExplorerLocations.ThisPc, addHistory: true);
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTab is not { HistoryIndex: > 0 } tab)
            return;
        var index = tab.HistoryIndex - 1;
        _ = NavigateAsync(tab, tab.History[index], addHistory: false, historyIndex: index);
    }

    private void Forward_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTab is not { } tab || tab.HistoryIndex >= tab.History.Count - 1)
            return;
        var index = tab.HistoryIndex + 1;
        _ = NavigateAsync(tab, tab.History[index], addHistory: false, historyIndex: index);
    }

    private void Files_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source
            && FindParent<DataGridRow>(source) is null)
            return;
        OpenSelectedEntry();
    }

    private void OpenSelectedEntry()
    {
        if (SelectedTab is not { } tab || Files.SelectedItem is not ExplorerEntry entry)
            return;

        if (entry.IsDirectory)
        {
            _ = NavigateAsync(tab, entry.FullPath, addHistory: true);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(entry.FullPath) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show($"파일을 열 수 없습니다.\n\n{ex.Message}", "PaneDock", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Files_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_changingTab && !_applyingSnapshot)
            UpdateStatus();
    }

    private void Pane_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        if (ctrl && key == Key.T)
            AddTab(SelectedTab?.Path ?? ExplorerLocations.ThisPc);
        else if (ctrl && key == Key.W && SelectedTab is { } current)
            CloseTab(current);
        else if (ctrl && key == Key.L)
        {
            BeginAddressEdit();
        }
        else if (ctrl && key == Key.F)
        {
            if (SelectedTab is { } tab)
            {
                tab.FilterVisible = true;
                SetFilterVisible(true);
                FilterBox.Focus();
                FilterBox.SelectAll();
                SessionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        else if (Keyboard.Modifiers == ModifierKeys.Alt && key == Key.Left)
            Back_Click(this, new RoutedEventArgs());
        else if (Keyboard.Modifiers == ModifierKeys.Alt && key == Key.Right)
            Forward_Click(this, new RoutedEventArgs());
        else if (Keyboard.Modifiers == ModifierKeys.Alt && key == Key.Up)
            Up_Click(this, new RoutedEventArgs());
        else if (key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None)
            Refresh_Click(this, new RoutedEventArgs());
        else if (key == Key.Escape && Address.IsVisible)
        {
            EndAddressEdit();
        }
        else if (key == Key.Escape && SelectedTab is { FilterVisible: true } active)
        {
            active.FilterVisible = false;
            SetFilterVisible(false);
            Files.Focus();
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (Files.IsKeyboardFocusWithin)
        {
            if (key == Key.Enter)
                OpenSelectedEntry();
            else if (key == Key.Back)
                Up_Click(this, new RoutedEventArgs());
            else if (!HandleFileKey(key))
                return;
        }
        else
            return;

        e.Handled = true;
    }

    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T found)
                return found;
            child = child is Visual ? VisualTreeHelper.GetParent(child) : LogicalTreeHelper.GetParent(child);
        }
        return null;
    }

    private void Activate_OnFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        Activated?.Invoke(this, EventArgs.Empty);

    private sealed class ExplorerTab
    {
        public string Path { get; set; } = "";
        public string RequestedPath { get; set; } = ExplorerLocations.ThisPc;
        public ObservableCollection<ExplorerEntry> Items { get; } = [];
        public ICollectionView View { get; }
        public List<ExplorerEntry> SelectedEntries { get; } = [];
        public TextBlock HeaderLabel { get; set; } = null!;
        public string FilterText { get; set; } = "";
        public bool FilterVisible { get; set; }
        public bool ShowHidden { get; set; }
        public string? SortColumn { get; set; }
        public bool SortDescending { get; set; }
        public bool IsLoading { get; set; }
        public bool IsCheckingPath { get; set; }
        public string? Error { get; set; }
        public List<string> History { get; } = [];
        public int HistoryIndex { get; set; } = -1;
        public int Generation { get; private set; }
        public CancellationTokenSource Cancellation { get; private set; } = new();
        public DirectoryChangeMonitor? Watcher { get; set; }
        public string? WatchError { get; set; }
        public bool RefreshRunning { get; set; }
        public bool RefreshQueued { get; set; }
        public bool IsDisposed { get; private set; }

        public ExplorerTab()
        {
            View = CollectionViewSource.GetDefaultView(Items);
            View.Filter = value => value is ExplorerEntry entry &&
                ExplorerItemFilter.IsVisible(entry, FilterText, ShowHidden);
        }

        public void CancelCurrentLoad()
        {
            Cancellation.Cancel();
            Cancellation.Dispose();
            Cancellation = new CancellationTokenSource();
            Generation++;
        }

        public void Dispose()
        {
            if (IsDisposed)
                return;
            IsDisposed = true;
            Watcher?.Dispose();
            Watcher = null;
            Cancellation.Cancel();
            Cancellation.Dispose();
            Generation++;
        }
    }
}
