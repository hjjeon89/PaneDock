using PaneDock.Core.Models;

namespace PaneDock.Core.Services;

/// <summary>Creates navigable path segments for drive and UNC paths without accessing the disk.</summary>
public static class PathBreadcrumb
{
    public static IReadOnlyList<BreadcrumbSegment> Build(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(root))
            throw new ArgumentException("드라이브 또는 네트워크 공유 경로가 필요합니다.", nameof(path));

        var rootLabel = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var result = new List<BreadcrumbSegment> { new(rootLabel.Length > 0 ? rootLabel : root, root) };
        var current = root;

        foreach (var part in fullPath[root.Length..].Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            result.Add(new BreadcrumbSegment(part, current));
        }

        return result;
    }
}
