namespace PaneDock.Core.Services;

public static class TabReorderService
{
    /// <summary>Moves one tab to the position occupied by the drop target.</summary>
    public static void Move<T>(IList<T> tabs, int from, int to)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        if (from < 0 || from >= tabs.Count || to < 0 || to >= tabs.Count)
            throw new ArgumentOutOfRangeException(nameof(from), "탭 이동 인덱스가 잘못되었습니다.");
        if (from == to) return;
        var tab = tabs[from];
        tabs.RemoveAt(from);
        tabs.Insert(to, tab);
    }
}
