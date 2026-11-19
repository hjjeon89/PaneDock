namespace PaneDock.Core.Models;

/// <summary>Saved panel paths and view arrangement for the next application launch.</summary>
public sealed class WorkspaceSession
{
    public int Version { get; set; } = 1;
    public PaneLayout Layout { get; set; } = PaneLayout.TwoColumns;
    public int ActivePaneNumber { get; set; } = 1;
    public List<PaneSession> Panes { get; set; } = [];
}

public sealed class PaneSession
{
    public List<string> TabPaths { get; set; } = [];
    public int ActiveTabIndex { get; set; }
}
