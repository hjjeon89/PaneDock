using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PaneDock.App.Controls;
using PaneDock.Core.Models;

namespace PaneDock.App;

public partial class MainWindow : Window
{
    private readonly ExplorerPane[] _panes;
    private PaneLayout _layout = PaneLayout.TwoColumns;
    private bool _lightTheme;
    private ExplorerPane? _activePane;

    public MainWindow()
    {
        InitializeComponent();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _panes = Enumerable.Range(1, 4).Select(i => new ExplorerPane(i, home)).ToArray();
        foreach (var pane in _panes)
        {
            pane.Activated += (_, _) => SetActivePane(pane);
            pane.FileLocationsChanged += folders =>
            {
                foreach (var other in _panes)
                    other.RefreshLocations(folders);
            };
        }

        ApplyLayout(_layout);
        SetActivePane(_panes[0]);
    }

    private void SetActivePane(ExplorerPane selected)
    {
        _activePane = selected;
        foreach (var pane in _panes)
            pane.SetActive(ReferenceEquals(pane, selected));
        Activity.Text = $"활성 패널: {selected.Number} · {PaneLayoutInfo.GetPaneCount(_layout)}개 패널";
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

    private void ApplyLayout(PaneLayout layout)
    {
        _layout = layout;
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
    }

    private static Grid NewGrid(int rows, int columns)
    {
        var grid = new Grid();
        for (var r = 0; r < rows * 2 - 1; r++)
            grid.RowDefinitions.Add(new RowDefinition
            {
                Height = r % 2 == 1 ? new GridLength(6) : new GridLength(1, GridUnitType.Star)
            });
        for (var c = 0; c < columns * 2 - 1; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = c % 2 == 1 ? new GridLength(6) : new GridLength(1, GridUnitType.Star)
            });
        if (rows == 2)
        {
            grid.Children.Add(new GridSplitter
            {
                Height = 6,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent,
                ResizeDirection = GridResizeDirection.Rows
            });
            Grid.SetRow(grid.Children[^1], 1);
            Grid.SetColumnSpan(grid.Children[^1], columns * 2 - 1);
        }
        if (columns == 2)
        {
            grid.Children.Add(new GridSplitter
            {
                Width = 6,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = Brushes.Transparent,
                ResizeDirection = GridResizeDirection.Columns
            });
            Grid.SetColumn(grid.Children[^1], 1);
            Grid.SetRowSpan(grid.Children[^1], rows * 2 - 1);
        }
        return grid;
    }

    private static void Place(Grid grid, UIElement child, int row, int column)
    {
        grid.Children.Add(child);
        Grid.SetRow(child, row);
        Grid.SetColumn(child, column);
    }

    private static void BuildCells(Grid host, int rows, int columns, params ExplorerPane[] panes)
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
    {
        _lightTheme = !_lightTheme;
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
    }
}
