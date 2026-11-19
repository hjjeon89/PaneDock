using PaneDock.Core.Models;

namespace PaneDock.Core.Services;

/// <summary>
/// Validates paths before a drag-and-drop copy. Never requests a move.
    /// Call with verifyExists=false for the frequently raised DragOver event,
    /// then verify again before invoking Windows Shell at Drop.
/// </summary>
public static class FileDropCopyPolicy
{
    public static string? GetRejectionReason(
        IEnumerable<string>? sources, string? destination, bool verifyExists = true)
    {
        if (sources is null)
            return "복사할 파일이나 폴더가 없습니다.";
        var sourceList = sources.ToArray();
        if (sourceList.Length == 0)
            return "복사할 파일이나 폴더가 없습니다.";
        if (string.IsNullOrWhiteSpace(destination) || ExplorerLocations.IsThisPc(destination))
            return "복사할 드라이브나 폴더를 선택하세요.";

        string fullDestination;
        try
        {
            if (!Path.IsPathFullyQualified(destination))
                return "대상 폴더는 절대 경로여야 합니다.";
            fullDestination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "대상 폴더 경로가 올바르지 않습니다.";
        }

        if (verifyExists && !Directory.Exists(fullDestination))
            return "대상 폴더가 없거나 접근할 수 없습니다.";

        foreach (var source in sourceList)
        {
            string fullSource;
            try
            {
                if (string.IsNullOrWhiteSpace(source) || !Path.IsPathFullyQualified(source))
                    return "복사할 항목의 경로가 올바르지 않습니다.";
                fullSource = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return "복사할 항목의 경로가 올바르지 않습니다.";
            }

            if (verifyExists && !File.Exists(fullSource) && !Directory.Exists(fullSource))
                return "복사할 파일 또는 폴더가 없어졌습니다.";

            // The Shell will deal with duplicate names in the same parent folder,
            // but a folder must never be copied into itself or its descendants.
            // Files can have no extension, and directory names can contain dots.
            // Do not guess the type from the name.
            var sourceIsFolder = verifyExists && Directory.Exists(fullSource);
            if (sourceIsFolder && IsInsideOrSame(fullDestination, fullSource))
                return "폴더를 자기 자신이나 하위 폴더에 복사할 수 없습니다.";
        }

        return null;
    }

    private static bool IsInsideOrSame(string path, string ancestor)
    {
        if (string.Equals(path, ancestor, StringComparison.OrdinalIgnoreCase))
            return true;
        var prefix = Path.EndsInDirectorySeparator(ancestor)
            ? ancestor : ancestor + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
