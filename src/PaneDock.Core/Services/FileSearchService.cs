using PaneDock.Core.Models;
using System.Diagnostics;

namespace PaneDock.Core.Services;

/// <summary>
/// Portable built-in file-name search. Does not require Everything SDK.
/// Traverses one folder at a time, skips inaccessible folders and reparse points,
/// reports batches, and honors cancellation and a configurable result cap.
/// </summary>
public sealed class FileSearchService
{
    public Task<FileSearchSummary> SearchAsync(
        string root, string query, bool recursive,
        IProgress<FileSearchProgress>? progress, CancellationToken token,
        int maxResults = 5000)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        return SearchAsync([root], query, recursive, progress, token, maxResults);
    }

    /// <summary>All mounted, ready drive roots. A missing search path can use these by default.</summary>
    public static IReadOnlyList<string> GetReadyDriveRoots()
    {
        var roots = new List<string>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.IsReady)
                    roots.Add(drive.RootDirectory.FullName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // An unplugged removable drive must not prevent searching other drives.
            }
        }
        return roots;
    }

    public Task<FileSearchSummary> SearchAsync(
        IEnumerable<string> roots, string query, bool recursive,
        IProgress<FileSearchProgress>? progress, CancellationToken token,
        int maxResults = 5000)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (maxResults is < 1 or > 100000)
            throw new ArgumentOutOfRangeException(nameof(maxResults));
        var rootList = roots.Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (rootList.Length == 0)
            throw new DirectoryNotFoundException("검색 가능한 드라이브나 폴더가 없습니다.");
        return Task.Run(() => Search(rootList, query.Trim(), recursive, progress, token, maxResults), token);
    }

    private static FileSearchSummary Search(
        IReadOnlyList<string> roots, string query, bool recursive,
        IProgress<FileSearchProgress>? progress, CancellationToken token, int limit)
    {
        var stack = new Stack<string>();
        foreach (var root in roots.Reverse())
        {
            if (Directory.Exists(root))
                stack.Push(root);
            else if (roots.Count == 1)
                throw new DirectoryNotFoundException(root);
        }
        var matches = 0;
        var scanned = 0;
        var skipped = roots.Count - stack.Count;
        var limitReached = false;
        var batch = new List<FileSearchResult>(128);
        var allResults = new List<FileSearchResult>(Math.Min(limit, 5000));
        var lastReport = Stopwatch.GetTimestamp();

        void Flush(string? currentFolder = null)
        {
            progress?.Report(new FileSearchProgress(batch.ToArray(), matches, scanned, skipped,
                limitReached, currentFolder));
            batch.Clear();
            lastReport = Stopwatch.GetTimestamp();
        }

        while (stack.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var current = stack.Pop();
            scanned++;
            try
            {
                foreach (var entry in new DirectoryInfo(current).EnumerateFileSystemInfos())
                {
                    token.ThrowIfCancellationRequested();
                    FileAttributes attributes;
                    try { attributes = entry.Attributes; }
                    catch (IOException) { continue; }
                    catch (UnauthorizedAccessException) { continue; }

                    var isDirectory = (attributes & FileAttributes.Directory) != 0;
                    var isLink = (attributes & FileAttributes.ReparsePoint) != 0;
                    if (recursive && isDirectory && !isLink)
                        stack.Push(entry.FullName);
                    if (!entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                        continue;

                    long? size = null;
                    if (!isDirectory)
                    {
                        try { size = ((FileInfo)entry).Length; }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                    DateTime modified;
                    try { modified = entry.LastWriteTime; }
                    catch (IOException) { modified = DateTime.MinValue; }
                    catch (UnauthorizedAccessException) { modified = DateTime.MinValue; }

                    var item = new FileSearchResult(entry.Name, entry.FullName, current,
                        isDirectory, size, modified);
                    batch.Add(item);
                    allResults.Add(item);
                    matches++;
                    if (batch.Count >= 128) Flush(current);
                    if (matches >= limit)
                    {
                        limitReached = true;
                        break;
                    }
                }
            }
            catch (IOException) { skipped++; }
            catch (UnauthorizedAccessException) { skipped++; }

            if (limitReached) break;
            if (Stopwatch.GetElapsedTime(lastReport) >= TimeSpan.FromMilliseconds(250))
                Flush(current);
        }

        Flush();
        return new FileSearchSummary(matches, scanned, skipped, limitReached, allResults);
    }
}
