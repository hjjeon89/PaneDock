using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
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
    private bool _changingTab;
    private bool _updatingFilterControls;
    private int _tabCounter;

    public event EventHandler? Activated;

    public int Number { get; }

    public ExplorerPane(int number, string initialPath)
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
        AddTab(initialPath);
    }

    public void SetActive(bool active)
    {
        OuterBorder.BorderBrush = active
            ? (Brush)FindResource("AccentBrush")
            : (Brush)FindResource("BorderBrush");
        OuterBorder.BorderThickness = active ? new Thickness(2) : new Thickness(1);
    }

    private ExplorerTab? SelectedTab => (Tabs.SelectedItem as TabItem)?.Tag as ExplorerTab;

    private void AddTab(string path)
    {
        var tab = new ExplorerTab();
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
        Tabs.Items.Add(new TabItem { Header = header, Tag = tab });
        Tabs.SelectedIndex = _tabs.Count - 1;
        _changingTab = false;
        ShowTab(tab);
        _ = NavigateAsync(tab, path, addHistory: true);
        Activated?.Invoke(this, EventArgs.Empty);
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
    }

    private async Task NavigateAsync(ExplorerTab tab, string path, bool addHistory, int? historyIndex = null)
    {
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
            fullPath = Path.GetFullPath(path);
            if (!await Task.Run(() => Directory.Exists(fullPath), token))
                throw new DirectoryNotFoundException($"폴더를 찾을 수 없습니다: {fullPath}");
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
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

        tab.IsCheckingPath = false;
        tab.Items.Clear();
        tab.SelectedEntries.Clear();
        tab.Path = fullPath;
        tab.HeaderLabel.Text = Path.GetFileName(Path.TrimEndingDirectorySeparator(fullPath)) is { Length: > 0 } name
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
        Address.Text = tab.Path;
        FilterBox.Text = tab.FilterText;
        ShowHidden.IsChecked = tab.ShowHidden;
        SetFilterVisible(tab.FilterVisible);
        _updatingFilterControls = false;
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
            + (tab.IsLoading ? " · 읽는 중…" : "");
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changingTab || !ReferenceEquals(e.Source, Tabs) || SelectedTab is not { } tab)
            return;
        ShowTab(tab);
        Activated?.Invoke(this, EventArgs.Empty);
    }

    private void NewTab_Click(object sender, RoutedEventArgs e) =>
        AddTab(SelectedTab?.Path ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

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
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingFilterControls || SelectedTab is not { } tab)
            return;
        tab.FilterText = FilterBox.Text;
        _filterTimer.Stop();
        _filterTimer.Start();
    }

    private void ShowHidden_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingFilterControls || SelectedTab is not { } tab)
            return;
        tab.ShowHidden = ShowHidden.IsChecked == true;
        tab.View.Refresh();
        UpdateStatus();
        _ = SyncFolderTreeAsync();
    }

    private async void Drives_Click(object sender, RoutedEventArgs e)
    {
        string[] drives;
        try
        {
            drives = await Task.Run(() => DriveInfo.GetDrives().Select(d => d.Name).ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status.Text = $"드라이브 목록 오류: {ex.Message}";
            return;
        }

        var menu = new ContextMenu { PlacementTarget = DrivesButton };
        var home = new MenuItem { Header = "⌂  사용자 폴더" };
        home.Click += (_, _) =>
        {
            if (SelectedTab is { } tab)
                _ = NavigateAsync(tab, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), addHistory: true);
        };
        menu.Items.Add(home);
        menu.Items.Add(new Separator());

        foreach (var root in drives)
        {
            var item = new MenuItem { Header = $"▣  {root}", Tag = root };
            item.Click += (_, _) =>
            {
                if (SelectedTab is { } tab)
                    _ = NavigateAsync(tab, root, addHistory: true);
            };
            menu.Items.Add(item);
        }

        DrivesButton.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private void Address_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || SelectedTab is not { } tab)
            return;
        e.Handled = true;
        _ = NavigateAsync(tab, Address.Text, addHistory: true);
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
        if (SelectedTab is not { } tab)
            return;
        var parent = Directory.GetParent(tab.Path);
        if (parent is not null)
            _ = NavigateAsync(tab, parent.FullName, addHistory: true);
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
        if (!_changingTab)
            UpdateStatus();
    }

    private void Pane_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        if (ctrl && key == Key.T)
            AddTab(SelectedTab?.Path ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        else if (ctrl && key == Key.W && SelectedTab is { } current)
            CloseTab(current);
        else if (ctrl && key == Key.L)
        {
            Address.Focus();
            Address.SelectAll();
        }
        else if (ctrl && key == Key.F)
        {
            if (SelectedTab is { } tab)
            {
                tab.FilterVisible = true;
                SetFilterVisible(true);
                FilterBox.Focus();
                FilterBox.SelectAll();
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
        else if (key == Key.Escape && SelectedTab is { FilterVisible: true } active)
        {
            active.FilterVisible = false;
            SetFilterVisible(false);
            Files.Focus();
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
        public ObservableCollection<ExplorerEntry> Items { get; } = [];
        public ICollectionView View { get; }
        public List<ExplorerEntry> SelectedEntries { get; } = [];
        public TextBlock HeaderLabel { get; set; } = null!;
        public string FilterText { get; set; } = "";
        public bool FilterVisible { get; set; }
        public bool ShowHidden { get; set; }
        public bool IsLoading { get; set; }
        public bool IsCheckingPath { get; set; }
        public string? Error { get; set; }
        public List<string> History { get; } = [];
        public int HistoryIndex { get; set; } = -1;
        public int Generation { get; private set; }
        public CancellationTokenSource Cancellation { get; private set; } = new();

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
            Cancellation.Cancel();
            Cancellation.Dispose();
            Generation++;
        }
    }
}
