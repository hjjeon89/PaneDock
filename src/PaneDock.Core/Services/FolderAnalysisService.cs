using PaneDock.Core.Models;
using System.Diagnostics;

namespace PaneDock.Core.Services;

/// <summary>
/// Recursively counts files and sizes for multiple selected folders.
/// Parent-child overlap and duplicate roots are removed before calculation.
/// Reparse-point directories are not traversed (prevents cycles/double counting).
/// </summary>
public sealed class FolderAnalysisService
{
    public Task<FolderAnalysisReport> AnalyzeAsync(
        IEnumerable<string> paths,
        IProgress<FolderAnalysisProgress>? progress, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var selected = paths.ToArray();
        return Task.Run(() => Analyze(selected, progress, token, silentCancellation: false)!, token);
    }

    // UI selection changes cancel the old analysis routinely. Return null
    // instead of throwing a first-chance exception on every panel switch.
    public Task<FolderAnalysisReport?> AnalyzeForUiAsync(
        IEnumerable<string> paths,
        IProgress<FolderAnalysisProgress>? progress, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var selected = paths.ToArray();
        return Task.Run(() => Analyze(selected, progress, token, silentCancellation: true));
    }

    public static IReadOnlyList<string> DistinctRoots(IEnumerable<string> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        var roots = selected.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path.Length)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase);

        var finalRoots = new List<string>();
        foreach (var path in roots)
        {
            if (finalRoots.Any(parent => IsDescendant(path, parent)))
                continue;
            finalRoots.Add(path);
        }
        return finalRoots;
    }

    private static bool IsDescendant(string child, string parent)
    {
        var prefix = parent.EndsWith(Path.DirectorySeparatorChar)
            ? parent : parent + Path.DirectorySeparatorChar;
        return child.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static FolderAnalysisReport? Analyze(string[] selected,
        IProgress<FolderAnalysisProgress>? progress, CancellationToken token,
        bool silentCancellation)
    {
        var validInput = selected.Count(value => !string.IsNullOrWhiteSpace(value));
        var roots = DistinctRoots(selected);
        var results = new List<FolderAnalysisItem>(roots.Count);

        foreach (var root in roots)
        {
            if (token.IsCancellationRequested)
            {
                if (silentCancellation) return null;
                token.ThrowIfCancellationRequested();
            }
            long files = 0, folders = 0, bytes = 0, skipped = 0;
            string? error = null;
            if (!Directory.Exists(root))
            {
                results.Add(new FolderAnalysisItem(root, 0, 0, 0, 0, "폴더가 없거나 접근할 수 없습니다."));
                continue;
            }

            var stack = new Stack<string>();
            stack.Push(root);
            var lastProgress = Stopwatch.GetTimestamp();
            while (stack.Count > 0)
            {
                if (token.IsCancellationRequested)
                {
                    if (silentCancellation) return null;
                    token.ThrowIfCancellationRequested();
                }
                var current = stack.Pop();
                try
                {
                    foreach (var info in new DirectoryInfo(current).EnumerateFileSystemInfos())
                    {
                        if (token.IsCancellationRequested)
                        {
                            if (silentCancellation) return null;
                            token.ThrowIfCancellationRequested();
                        }
                        FileAttributes attributes;
                        try { attributes = info.Attributes; }
                        catch (IOException) { skipped++; continue; }
                        catch (UnauthorizedAccessException) { skipped++; continue; }

                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            skipped++;
                            continue;
                        }
                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            folders++;
                            stack.Push(info.FullName);
                        }
                        else
                        {
                            files++;
                            try { bytes += ((FileInfo)info).Length; }
                            catch (IOException) { skipped++; }
                            catch (UnauthorizedAccessException) { skipped++; }
                        }
                        if ((files + folders) % 128 == 0
                            && Stopwatch.GetElapsedTime(lastProgress) >= TimeSpan.FromMilliseconds(120))
                        {
                            progress?.Report(new FolderAnalysisProgress(current, files, folders, bytes));
                            lastProgress = Stopwatch.GetTimestamp();
                        }
                    }
                }
                catch (IOException) { skipped++; }
                catch (UnauthorizedAccessException) { skipped++; }
            }
            var item = new FolderAnalysisItem(root, files, folders, bytes, skipped, error);
            results.Add(item);
            progress?.Report(new FolderAnalysisProgress(root, files, folders, bytes, item));
        }

        return new FolderAnalysisReport(results, validInput - roots.Count);
    }
}
