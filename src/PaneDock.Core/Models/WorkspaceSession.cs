namespace PaneDock.Core.Models;

/// <summary>Saved panel paths and view arrangement for the next application launch.</summary>
public sealed class WorkspaceSession
{
    public int Version { get; set; } = 1;
    public PaneLayout Layout { get; set; } = PaneLayout.TwoColumns;
    public int ActivePaneNumber { get; set; } = 1;
    // False by default to retain Light on first launch and for old saved sessions.
    public bool IsDarkTheme { get; set; }
    // Separate ratios per layout, so switching between layouts does not discard sizing.
    public Dictionary<PaneLayout, PaneSplitRatios> LayoutRatios { get; set; } = [];
    public List<PaneSession> Panes { get; set; } = [];
}

public sealed class PaneSplitRatios
{
    public double FirstColumn { get; set; } = 0.5;
    public double FirstRow { get; set; } = 0.5;
}

public sealed class PaneSession
{
    public List<string> TabPaths { get; set; } = [];
    public int ActiveTabIndex { get; set; }
}
