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
                ? session.ActivePaneNumber : 1
        };

        foreach (var pane in (session.Panes ?? []).Take(4))
        {
            var paths = (pane?.TabPaths ?? []).Take(MaxTabsPerPane)
                .Select(SafePath).ToList();
            if (paths.Count == 0)
                paths.Add(ExplorerLocations.ThisPc);

            result.Panes.Add(new PaneSession
            {
                TabPaths = paths,
                ActiveTabIndex = Math.Clamp(pane?.ActiveTabIndex ?? 0, 0, paths.Count - 1)
            });
        }

        while (result.Panes.Count < 4)
            result.Panes.Add(new PaneSession { TabPaths = [ExplorerLocations.ThisPc] });

        if (result.ActivePaneNumber > PaneLayoutInfo.GetPaneCount(result.Layout))
            result.ActivePaneNumber = 1;

        return result;
    }

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
