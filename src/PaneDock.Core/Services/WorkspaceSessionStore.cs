using System.Text.Json;
using System.Text.Json.Serialization;
using PaneDock.Core.Models;

namespace PaneDock.Core.Services;

/// <summary>
/// Stores one user's last workspace without touching the application's build folder.
/// A missing or damaged file is treated as a first launch.
/// </summary>
public sealed class WorkspaceSessionStore
{
    private const int CurrentVersion = 1;
    private const int MaxTabsPerPane = 50;
    private const long MaxFileBytes = 1024 * 1024;
    private static readonly HashSet<string> SortColumns =
        ["Name", "Extension", "Size", "Kind", "ModifiedAt", "CreatedAt"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string FilePath { get; }

    public WorkspaceSessionStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PaneDock", "workspace.json");
    }

    public WorkspaceSession? Load()
    {
        try
        {
            if (!File.Exists(FilePath) || new FileInfo(FilePath).Length > MaxFileBytes)
                return null;

            var saved = JsonSerializer.Deserialize<WorkspaceSession>(
                File.ReadAllText(FilePath), JsonOptions);
            return saved is { Version: CurrentVersion } ? Normalize(saved) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException
                                   or UnauthorizedAccessException or ArgumentException
                                   or NotSupportedException)
        {
            return null;
        }
    }

    public void Save(WorkspaceSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var safe = Normalize(session);
        var directory = Path.GetDirectoryName(Path.GetFullPath(FilePath))!;
        Directory.CreateDirectory(directory);

        // The temporary file is on the same volume as the destination:
        // replacing it does not leave a half-written JSON after a crash.
        var tempPath = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(safe, JsonOptions));
            File.Move(tempPath, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    public static WorkspaceSession Normalize(WorkspaceSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var result = new WorkspaceSession
        {
            Layout = Enum.IsDefined(session.Layout) ? session.Layout : PaneLayout.TwoColumns,
            ActivePaneNumber = session.ActivePaneNumber is >= 1 and <= 4
                ? session.ActivePaneNumber : 1,
            IsDarkTheme = session.IsDarkTheme,
            WindowPlacement = SafeWindowPlacement(session.WindowPlacement)
        };

        foreach (var (layout, ratios) in session.LayoutRatios ?? [])
        {
            if (!Enum.IsDefined(layout) || ratios is null)
                continue;

            result.LayoutRatios[layout] = new PaneSplitRatios
            {
                FirstColumn = SafeRatio(ratios.FirstColumn),
                FirstRow = SafeRatio(ratios.FirstRow)
            };
        }

        foreach (var pane in (session.Panes ?? []).Take(4))
        {
            var paths = (pane?.TabPaths ?? []).Take(MaxTabsPerPane)
                .Select(SafePath).ToList();
            if (paths.Count == 0)
                paths.Add(ExplorerLocations.ThisPc);

            result.Panes.Add(new PaneSession
            {
                TabPaths = paths,
                ActiveTabIndex = Math.Clamp(pane?.ActiveTabIndex ?? 0, 0, paths.Count - 1),
                TabStates = Enumerable.Range(0, paths.Count).Select(index =>
                    SafeTabState(pane?.TabStates?.ElementAtOrDefault(index))).ToList()
            });
        }

        while (result.Panes.Count < 4)
            result.Panes.Add(new PaneSession
            {
                TabPaths = [ExplorerLocations.ThisPc],
                TabStates = [new TabViewState()]
            });

        if (result.ActivePaneNumber > PaneLayoutInfo.GetPaneCount(result.Layout))
            result.ActivePaneNumber = 1;

        return result;
    }

    private static double SafeRatio(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0.1, 0.9) : 0.5;

    private static WindowPlacementSession? SafeWindowPlacement(WindowPlacementSession? value)
    {
        if (value is null || !double.IsFinite(value.Left) || !double.IsFinite(value.Top)
            || !double.IsFinite(value.Width) || !double.IsFinite(value.Height)
            || value.Width < 760 || value.Height < 550 || value.Width > 10000
            || value.Height > 10000 || Math.Abs(value.Left) > 100000
            || Math.Abs(value.Top) > 100000)
            return null;

        return new WindowPlacementSession
        {
            Left = value.Left,
            Top = value.Top,
            Width = value.Width,
            Height = value.Height,
            IsMaximized = value.IsMaximized
        };
    }

    private static TabViewState SafeTabState(TabViewState? state) => new()
    {
        FilterText = state?.FilterText is { } filter ? filter[..Math.Min(filter.Length, 256)] : "",
        FilterVisible = state?.FilterVisible ?? false,
        ShowHidden = state?.ShowHidden ?? false,
        SortColumn = state?.SortColumn is { } column && SortColumns.Contains(column)
            ? column : null,
        SortDescending = state?.SortDescending ?? false
    };

    private static string SafePath(string? path)
    {
        if (ExplorerLocations.IsThisPc(path))
            return ExplorerLocations.ThisPc;

        if (string.IsNullOrWhiteSpace(path) || path.Length > 32767)
            return ExplorerLocations.ThisPc;

        try
        {
            return Path.IsPathFullyQualified(path) ? Path.GetFullPath(path)
                : ExplorerLocations.ThisPc;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException
                                   or PathTooLongException)
        {
            return ExplorerLocations.ThisPc;
        }
    }
}
