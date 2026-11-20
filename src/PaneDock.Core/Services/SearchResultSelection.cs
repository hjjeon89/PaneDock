using PaneDock.Core.Models;

namespace PaneDock.Core.Services;

/// <summary>
/// Normalizes multi-selected search results before deleting them and removes
/// entries that were actually deleted, including children of deleted folders.
/// No filesystem changes are made here.
/// </summary>
public static class SearchResultSelection
{
    public static IReadOnlyList<FileSearchResult> DeletionTargets(
        IEnumerable<FileSearchResult> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        var unique = selected.Where(item => IsSafeItemPath(item.FullPath))
            .DistinctBy(item => Normalize(item.FullPath), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var directories = unique.Where(item => item.IsDirectory).ToArray();

        // Deleting the parent already includes its children. Never queue both.
        return unique.Where(item => !directories.Any(parent =>
            !ReferenceEquals(parent, item)
            && IsDescendant(item.FullPath, parent.FullPath))).ToArray();
    }

    public static IReadOnlyList<FileSearchResult> WithoutDeleted(
        IEnumerable<FileSearchResult> results,
        IEnumerable<FileSearchResult> deleted)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(deleted);
        var gone = deleted.ToArray();
        return results.Where(item => !gone.Any(removed =>
            SamePath(item.FullPath, removed.FullPath)
            || removed.IsDirectory && IsDescendant(item.FullPath, removed.FullPath))).ToArray();
    }

    private static bool IsSafeItemPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            return false;
        var root = Path.GetPathRoot(path);
        return !string.IsNullOrWhiteSpace(root) && !SamePath(path, root);
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(path);

    private static bool SamePath(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    private static bool IsDescendant(string child, string parent)
    {
        var normalizedParent = Normalize(parent);
        return child.StartsWith(normalizedParent.EndsWith(Path.DirectorySeparatorChar)
                ? normalizedParent : normalizedParent + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }
}
