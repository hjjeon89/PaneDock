namespace PaneDock.Core.Models;

public enum PaneLayout
{
    Single,
    TwoColumns,
    TwoRows,
    ThreeLeft,
    ThreeTop,
    FourGrid
}

public static class PaneLayoutInfo
{
    public static int GetPaneCount(PaneLayout layout) => layout switch
    {
        PaneLayout.Single => 1,
        PaneLayout.TwoColumns or PaneLayout.TwoRows => 2,
        PaneLayout.ThreeLeft or PaneLayout.ThreeTop => 3,
        PaneLayout.FourGrid => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(layout), layout, "알 수 없는 패널 레이아웃입니다.")
    };
}
