using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PaneDock.App.Controls;
using PaneDock.Core.Models;
using PaneDock.Core.Services;

namespace PaneDock.App;

public partial class MainWindow : Window
{
    private readonly ExplorerPane[] _panes;
    private readonly WorkspaceSessionStore _sessionStore = new();
    private readonly DispatcherTimer _sessionSaveTimer;
    private readonly Dictionary<PaneLayout, PaneSplitRatios> _layoutRatios = [];
    private Grid? _columnSplitGrid;
    private Grid? _rowSplitGrid;
    private PaneLayout _layout = PaneLayout.TwoColumns;
    private bool _lightTheme = true;
    private bool _initializing = true;
    private ExplorerPane? _activePane;

    public MainWindow()
    {
        InitializeComponent();
        var session = _sessionStore.Load();
        RestoreWindowPlacement(session?.WindowPlacement);
        if (session is not null)
        {
            foreach (var (layout, ratio) in session.LayoutRatios)
                _layoutRatios[layout] = new PaneSplitRatios
                {
                    FirstColumn = ratio.FirstColumn,
                    FirstRow = ratio.FirstRow
                };
        }
        _sessionSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _sessionSaveTimer.Tick += (_, _) =>
        {
            _sessionSaveTimer.Stop();
            SaveSession();
        };

        ApplyTheme(session?.IsDarkTheme == true);
        _panes = Enumerable.Range(1, 4)
            .Select(i => new ExplorerPane(i, session?.Panes.ElementAtOrDefault(i - 1))).ToArray();
        foreach (var pane in _panes)
        {
            pane.Activated += (_, _) => SetActivePane(pane);
            pane.SessionChanged += (_, _) => ScheduleSessionSave();
            pane.MoveTabRequested += (source, target) =>
            {
                if (target is < 1 or > 4 || source.Number == target)
                    return;
                var transfer = source.GetActiveTabTransfer();
                if (transfer is null) return;
                if (target > PaneLayoutInfo.GetPaneCount(_layout))
                    ApplyLayout(target switch
                    {
                        2 => PaneLayout.TwoColumns,
                        3 => PaneLayout.ThreeLeft,
                        _ => PaneLayout.FourGrid
                    });
                _panes[target - 1].ReceiveTab(transfer.Value.Path, transfer.Value.State);
                source.FinishTabMove();
                SetActivePane(_panes[target - 1]);
                ScheduleSessionSave();
            };
            pane.FileLocationsChanged += folders =>
            {
                foreach (var other in _panes)
                    other.RefreshLocations(folders);
            };
        }

        ApplyLayout(session?.Layout ?? _layout);
        var activePaneNumber = session?.ActivePaneNumber ?? 1;
        SetActivePane(_panes[Math.Clamp(activePaneNumber - 1, 0, _panes.Length - 1)]);
        _initializing = false;
        LocationChanged += (_, _) => ScheduleSessionSave();
        SizeChanged += (_, _) => ScheduleSessionSave();
        StateChanged += (_, _) => ScheduleSessionSave();
        Closing += (_, _) =>
        {
            _sessionSaveTimer.Stop();
            SaveSession();
        };
        Closed += (_, _) =>
        {
            foreach (var pane in _panes)
                pane.Dispose();
        };
    }

    private void SetActivePane(ExplorerPane selected)
    {
        _activePane = selected;
        foreach (var pane in _panes)
            pane.SetActive(ReferenceEquals(pane, selected));
        Activity.Text = $"활성 패널: {selected.Number} · {PaneLayoutInfo.GetPaneCount(_layout)}개 패널";
        ScheduleSessionSave();
    }

    private void Layout_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name }
            && Enum.TryParse(name, out PaneLayout layout))
            ApplyLayout(layout);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(this);
        else
            SystemCommands.MaximizeWindow(this);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    private void OpenSearch_Click(object sender, RoutedEventArgs e) => OpenSearch();

    private void FolderAnalysis_Click(object sender, RoutedEventArgs e)
    {
        var pane = _activePane ?? _panes[0];
        var folders = pane.GetFoldersForAnalysis();
        var window = new FolderAnalysisWindow(folders) { Owner = this };
        window.Show();
    }

    private void GlobalSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        OpenSearch();
    }

    private void OpenSearch()
    {
        var pane = _activePane ?? _panes[0];
        var root = pane.CurrentPath;
        if (ExplorerLocations.IsThisPc(root))
            root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var search = new SearchWindow(root, GlobalSearchBox.Text) { Owner = this };
        search.NavigateRequested += path =>
        {
            pane.NavigateTo(path);
            search.Close();
        };
        search.Show();
    }

    private void ApplyLayout(PaneLayout layout)
    {
        CaptureCurrentLayoutRatios();
        _layout = layout;
        _columnSplitGrid = null;
        _rowSplitGrid = null;
        // WPF 컨트롤의 논리적 부모는 하나만 허용된다.
        // 레이아웃 변경 시 뷰를 보존한 채 부모 컨테이너만 재배치한다.
        RemovePaneParents(PaneHost);
        PaneHost.Children.Clear();
        PaneHost.RowDefinitions.Clear();
        PaneHost.ColumnDefinitions.Clear();

        switch (layout)
        {
            case PaneLayout.Single:
                BuildCells(PaneHost, 1, 1, _panes[0]);
                break;
            case PaneLayout.TwoColumns:
                BuildCells(PaneHost, 1, 2, _panes[0], _panes[1]);
                break;
            case PaneLayout.TwoRows:
                BuildCells(PaneHost, 2, 1, _panes[0], _panes[1]);
                break;
            case PaneLayout.FourGrid:
                BuildCells(PaneHost, 2, 2, _panes);
                break;
            case PaneLayout.ThreeLeft:
                var left = NewGrid(1, 2);
                PaneHost.Children.Add(left);
                Place(left, _panes[0], 0, 0);
                var right = NewGrid(2, 1);
                Place(left, right, 0, 2);
                Place(right, _panes[1], 0, 0);
                Place(right, _panes[2], 2, 0);
                break;
            case PaneLayout.ThreeTop:
                var vertical = NewGrid(2, 1);
                PaneHost.Children.Add(vertical);
                Place(vertical, _panes[0], 0, 0);
                var bottom = NewGrid(1, 2);
                Place(vertical, bottom, 2, 0);
                Place(bottom, _panes[1], 0, 0);
                Place(bottom, _panes[2], 0, 2);
                break;
        }

        if (_activePane is not null)
        {
            var visibleCount = PaneLayoutInfo.GetPaneCount(layout);
            SetActivePane(_activePane.Number <= visibleCount ? _activePane : _panes[0]);
        }
        ScheduleSessionSave();
    }

    private void ScheduleSessionSave()
    {
        if (_initializing)
            return;
        _sessionSaveTimer.Stop();
        _sessionSaveTimer.Start();
    }

    private void SaveSession()
    {
        if (_initializing)
            return;
        try
        {
            CaptureCurrentLayoutRatios();
            _sessionStore.Save(new WorkspaceSession
            {
                Layout = _layout,
                ActivePaneNumber = _activePane?.Number ?? 1,
                IsDarkTheme = !_lightTheme,
                LayoutRatios = _layoutRatios.ToDictionary(
                    pair => pair.Key, pair => new PaneSplitRatios
                    {
                        FirstColumn = pair.Value.FirstColumn,
                        FirstRow = pair.Value.FirstRow
                }),
                WindowPlacement = CaptureWindowPlacement(),
                Panes = _panes.Select(pane => pane.CaptureSession()).ToList()
            });
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException
                                   or ArgumentException or NotSupportedException)
        {
            Trace.WriteLine($"PaneDock 작업공간 저장 실패: {ex}");
        }
    }

    private void RestoreWindowPlacement(WindowPlacementSession? placement)
    {
        if (placement is null) return;

        var visible = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var requested = new Rect(placement.Left, placement.Top, placement.Width, placement.Height);
        var intersection = Rect.Intersect(visible, requested);
        // A disconnected monitor can leave windows entirely outside the desktop.
        // Falling back to the centered default is safer than a hidden window.
        if (intersection.IsEmpty || intersection.Width < 180 || intersection.Height < 100)
            return;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = placement.Left;
        Top = placement.Top;
        Width = placement.Width;
        Height = placement.Height;
        if (placement.IsMaximized)
            Loaded += (_, _) => WindowState = WindowState.Maximized;
    }

    private WindowPlacementSession CaptureWindowPlacement()
    {
        var bounds = WindowState != WindowState.Normal ? RestoreBounds
            : new Rect(Left, Top, ActualWidth, ActualHeight);
        if (bounds.IsEmpty || !double.IsFinite(bounds.Left) || !double.IsFinite(bounds.Top)
            || bounds.Width < MinWidth || bounds.Height < MinHeight)
            bounds = new Rect(100, 100, Math.Max(MinWidth, Width), Math.Max(MinHeight, Height));

        return new WindowPlacementSession
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            IsMaximized = WindowState == WindowState.Maximized
        };
    }

    private void CaptureCurrentLayoutRatios()
    {
        if (_columnSplitGrid is null && _rowSplitGrid is null)
            return;

        if (!_layoutRatios.TryGetValue(_layout, out var ratios))
        {
            ratios = new PaneSplitRatios();
            _layoutRatios[_layout] = ratios;
        }

        if (_columnSplitGrid is { ColumnDefinitions.Count: >= 3 } columns)
        {
            var first = columns.ColumnDefinitions[0].ActualWidth;
            var second = columns.ColumnDefinitions[2].ActualWidth;
            if (first + second > 1)
                ratios.FirstColumn = Math.Clamp(first / (first + second), 0.1, 0.9);
        }

        if (_rowSplitGrid is { RowDefinitions.Count: >= 3 } rows)
        {
            var first = rows.RowDefinitions[0].ActualHeight;
            var second = rows.RowDefinitions[2].ActualHeight;
            if (first + second > 1)
                ratios.FirstRow = Math.Clamp(first / (first + second), 0.1, 0.9);
        }
    }

    private Grid NewGrid(int rows, int columns)
    {
        _layoutRatios.TryGetValue(_layout, out var saved);
        var columnRatio = saved?.FirstColumn ?? 0.5;
        var rowRatio = saved?.FirstRow ?? 0.5;
        var grid = new Grid();
        for (var r = 0; r < rows * 2 - 1; r++)
            grid.RowDefinitions.Add(new RowDefinition
            {
                Height = r % 2 == 1 ? new GridLength(6)
                    : new GridLength(r == 0 ? rowRatio : 1 - rowRatio, GridUnitType.Star)
            });
        for (var c = 0; c < columns * 2 - 1; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = c % 2 == 1 ? new GridLength(6)
                    : new GridLength(c == 0 ? columnRatio : 1 - columnRatio, GridUnitType.Star)
            });
        if (rows == 2)
        {
            _rowSplitGrid = grid;
            var splitter = new GridSplitter
            {
                Height = 6,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent,
                ResizeDirection = GridResizeDirection.Rows
            };
            splitter.DragCompleted += (_, _) => SplitterDragCompleted();
            splitter.KeyUp += SplitterKeyUp;
            grid.Children.Add(splitter);
            Grid.SetRow(grid.Children[^1], 1);
            Grid.SetColumnSpan(grid.Children[^1], columns * 2 - 1);
        }
        if (columns == 2)
        {
            _columnSplitGrid = grid;
            var splitter = new GridSplitter
            {
                Width = 6,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = Brushes.Transparent,
                ResizeDirection = GridResizeDirection.Columns
            };
            splitter.DragCompleted += (_, _) => SplitterDragCompleted();
            splitter.KeyUp += SplitterKeyUp;
            grid.Children.Add(splitter);
            Grid.SetColumn(grid.Children[^1], 1);
            Grid.SetRowSpan(grid.Children[^1], rows * 2 - 1);
        }
        return grid;
    }

    private void SplitterDragCompleted()
    {
        CaptureCurrentLayoutRatios();
        ScheduleSessionSave();
    }

    private void SplitterKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
            SplitterDragCompleted();
    }

    private static void Place(Grid grid, UIElement child, int row, int column)
    {
        grid.Children.Add(child);
        Grid.SetRow(child, row);
        Grid.SetColumn(child, column);
    }

    private void BuildCells(Grid host, int rows, int columns, params ExplorerPane[] panes)
    {
        var layout = NewGrid(rows, columns);
        host.Children.Add(layout);
        for (var i = 0; i < panes.Length; i++)
            Place(layout, panes[i], i / columns * 2, i % columns * 2);
    }

    private static void RemovePaneParents(DependencyObject node)
    {
        if (node is Panel panel)
        {
            foreach (var child in panel.Children.OfType<DependencyObject>().ToArray())
            {
                if (child is ExplorerPane)
                    panel.Children.Remove((UIElement)child);
                else
                    RemovePaneParents(child);
            }
        }
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
        => ApplyTheme(_lightTheme);

    private void ApplyTheme(bool isDark)
    {
        _lightTheme = !isDark;
        ThemeButton.Content = _lightTheme ? "◐ Light" : "◐ Dark";
        ThemeButton.ToolTip = _lightTheme
            ? "현재 Light · 클릭하면 Dark로 변경"
            : "현재 Dark · 클릭하면 Light로 변경";
        var resources = Application.Current.Resources;
        void Set(string key, string hex) =>
            resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

        if (_lightTheme)
        {
            Set("WindowBackground", "#F2F5FA");
            Set("CardBackground", "#FFFFFF");
            Set("PanelBackground", "#F8FAFD");
            Set("BorderBrush", "#CAD5E3");
            Set("TextPrimary", "#172338");
            Set("TextSecondary", "#52667E");
            Set("AccentBrush", "#1973C8");
            Set("SelectionBrush", "#C7E3FF");
        }
        else
        {
            Set("WindowBackground", "#111827");
            Set("CardBackground", "#1E293B");
            Set("PanelBackground", "#172234");
            Set("BorderBrush", "#344357");
            Set("TextPrimary", "#F2F6FB");
            Set("TextSecondary", "#9DB0C8");
            Set("AccentBrush", "#4DADFF");
            Set("SelectionBrush", "#254E74");
        }

        if (_activePane is not null)
            SetActivePane(_activePane);
        ScheduleSessionSave();
    }
}
