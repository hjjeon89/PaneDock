namespace PaneDock.Core.Models;

/// <summary>Virtual locations that do not correspond to a physical directory.</summary>
public static class ExplorerLocations
{
    public const string ThisPc = "pane-dock://this-pc";

    public static bool IsThisPc(string? path) =>
        string.Equals(path, ThisPc, StringComparison.Ordinal);
}
