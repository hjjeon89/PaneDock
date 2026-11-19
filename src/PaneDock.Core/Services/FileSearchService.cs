using PaneDock.Core.Models;

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
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (maxResults is < 1 or > 100000)
            throw new ArgumentOutOfRangeException(nameof(maxResults));
        return Task.Run(() => Search(root, query.Trim(), recursive, progress, token, maxResults), token);
    }

    private static FileSearchSummary Search(
        string root, string query, bool recursive,
        IProgress<FileSearchProgress>? progress, CancellationToken token, int limit)
    {
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(root);

        var stack = new Stack<string>();
        stack.Push(Path.GetFullPath(root));
        var matches = 0;
        var scanned = 0;
        var skipped = 0;
        var limitReached = false;
        var batch = new List<FileSearchResult>(128);

        void Flush()
        {
            if (batch.Count == 0) return;
            progress?.Report(new FileSearchProgress(batch.ToArray(), matches, scanned, skipped, limitReached));
            batch.Clear();
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

                    batch.Add(new FileSearchResult(entry.Name, entry.FullName, current,
                        isDirectory, size, modified));
                    matches++;
                    if (batch.Count >= 128) Flush();
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
        }

        Flush();
        progress?.Report(new FileSearchProgress([], matches, scanned, skipped, limitReached));
        return new FileSearchSummary(matches, scanned, skipped, limitReached);
    }
}
