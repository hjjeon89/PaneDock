using PaneDock.Core.Models;

namespace PaneDock.Core.Services;

/// <summary>현재 폴더 목록에만 적용하는 UI 필터입니다. 전체 PC 검색과 무관합니다.</summary>
public static class ExplorerItemFilter
{
    public static bool IsVisible(ExplorerEntry item, string? text, bool showHidden)
    {
        ArgumentNullException.ThrowIfNull(item);

        // Drive roots must remain selectable in the virtual "내 PC" location,
        // even when a tab retains a filter from the previously visited folder.
        if (item.IsDrive)
            return true;

        if (!showHidden && item.IsHidden)
            return false;

        return string.IsNullOrWhiteSpace(text)
            || item.Name.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
