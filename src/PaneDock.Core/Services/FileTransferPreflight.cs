namespace PaneDock.Core.Services;

/// <summary>
/// Non-destructive validation for Shell copy/move operations. This is only
/// a preflight; Windows Shell still owns conflict resolution and permissions.
/// </summary>
public static class FileTransferPreflight
{
    public sealed record Result(string? Error, IReadOnlyList<string> Collisions)
    {
        public bool CanProceed => Error is null;
    }

    public static Result Check(IEnumerable<string>? sources, string? destination,
        bool moving = false)
    {
        if (sources is null)
            return new Result("복사하거나 이동할 파일이 없습니다.", []);
        var paths = sources.ToArray();
        if (paths.Length == 0)
            return new Result("복사하거나 이동할 파일이 없습니다.", []);
        if (paths.Any(string.IsNullOrWhiteSpace))
            return new Result("원본 경로가 비어 있는 항목이 포함되어 있습니다.", []);

        string target;
        try
        {
            if (string.IsNullOrWhiteSpace(destination)
                || !Path.IsPathFullyQualified(destination))
                return new Result("대상 폴더 경로가 유효하지 않습니다.", []);
            target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
            if (!Directory.Exists(target))
                return new Result("대상 폴더가 없거나 연결이 끊겼습니다.", []);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return new Result("대상 폴더 경로가 유효하지 않습니다.", []);
        }

        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selectedFolders = new List<string>();
        var collisions = new List<string>();
        foreach (var path in paths)
        {
            string source;
            try
            {
                if (!Path.IsPathFullyQualified(path))
                    return new Result("잘못된 원본 경로가 포함되어 있습니다.", []);
                source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
                var root = Path.TrimEndingDirectorySeparator(Path.GetPathRoot(source)!);
                if (string.Equals(source, root, StringComparison.OrdinalIgnoreCase))
                    return new Result("드라이브 전체를 복사하거나 이동할 수 없습니다.", []);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
            {
                return new Result("잘못된 원본 경로가 포함되어 있습니다.", []);
            }

            if (!unique.Add(source))
                return new Result("원본 항목이 중복되어 있습니다.", []);

            var folder = Directory.Exists(source);
            if (!folder && !File.Exists(source))
                return new Result("원본이 없어졌거나 접근할 수 없습니다: " + source, []);

            if (folder)
                selectedFolders.Add(source);
            if (folder && IsSameOrDescendant(target, source))
                return new Result("폴더를 자기 자신이나 하위 폴더로 복사/이동할 수 없습니다.", []);
            var filename = Path.GetFileName(source);
            if (!targetNames.Add(filename))
                return new Result("동일한 이름의 원본 항목이 여러 개 선택되었습니다: "
                    + filename + ". 이름을 구분한 뒤 다시 시도하세요.", []);
            var childTarget = Path.Combine(target, filename);
            if (moving && string.Equals(source, childTarget, StringComparison.OrdinalIgnoreCase))
                return new Result("원본과 대상이 같은 폴더입니다.", []);

            if (File.Exists(childTarget) || Directory.Exists(childTarget))
                collisions.Add(childTarget);
        }

        // Clipboard or external drops can contain both a folder and a child
        // of that folder. Queuing them together risks confusing partial moves
        // after the parent has already been moved.
        if (unique.Any(source => selectedFolders.Any(folder =>
                !source.Equals(folder, StringComparison.OrdinalIgnoreCase)
                && IsSameOrDescendant(source, folder))))
            return new Result("폴더와 그 하위 항목을 동시에 복사하거나 이동할 수 없습니다.", []);

        return new Result(null, collisions);
    }

    private static bool IsSameOrDescendant(string target, string folder) =>
        string.Equals(target, folder, StringComparison.OrdinalIgnoreCase)
        || target.StartsWith(Path.EndsInDirectorySeparator(folder)
            ? folder : folder + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
}
