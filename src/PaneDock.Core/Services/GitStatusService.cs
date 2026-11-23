using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using PaneDock.Core.Models;

namespace PaneDock.Core.Services;

/// <summary>
/// Reads working-tree, index and conflict status with the installed Git CLI.
/// Queries never block the UI thread, modify the repository or contact remotes.
/// One short-lived cache is shared between all four explorer panes.
/// </summary>
public sealed class GitStatusService
{
    private sealed class CachedRepository
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public DateTime LastRead;
        public GitRepositorySnapshot? Snapshot;
    }

    private static readonly ConcurrentDictionary<string, CachedRepository> Cache =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(3);

    public async Task<GitRepositorySnapshot?> GetSnapshotAsync(string directory, bool force = false)
    {
        if (string.IsNullOrWhiteSpace(directory) || ExplorerLocations.IsThisPc(directory))
            return null;

        // Even finding the .git directory can be slow on disconnected shares.
        var root = await Task.Run(() => FindRepositoryRoot(directory));
        if (root is null)
            return null;

        var entry = Cache.GetOrAdd(root, _ => new CachedRepository());
        await entry.Gate.WaitAsync();
        try
        {
            if (!force && DateTime.UtcNow - entry.LastRead < CacheLifetime)
                return entry.Snapshot;

            var status = await RunGitAsync(root,
                ["status", "--porcelain=v1", "-z", "--branch", "--untracked-files=all"]);
            var tracked = status is null ? null : await RunGitAsync(root,
                ["ls-files", "--cached", "-z"]);
            var result = status is not null && tracked is not null
                ? Parse(root, status, tracked)
                : null;
            entry.Snapshot = result;
            entry.LastRead = DateTime.UtcNow;
            return result;
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    public static string? FindRepositoryRoot(string path)
    {
        try
        {
            var directory = new DirectoryInfo(Path.GetFullPath(path));
            for (; directory is not null; directory = directory.Parent)
            {
                var git = Path.Combine(directory.FullName, ".git");
                if (Directory.Exists(git) || File.Exists(git))
                    return Path.TrimEndingDirectorySeparator(directory.FullName);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or ArgumentException or NotSupportedException)
        {
            // A missing/inaccessible directory is not an application error.
        }
        return null;
    }

    /// <summary>
    /// Parses porcelain v1 -z without splitting on spaces, quotes or newlines
    /// in filenames. Rename/copy records have an additional NUL-delimited name.
    /// </summary>
    public static GitRepositorySnapshot Parse(string root, string porcelain, string trackedPaths)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var changed = new Dictionary<string, GitPathStatus>(StringComparer.OrdinalIgnoreCase);
        var tracked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var trackedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
        var folderChanges = new Dictionary<string, GitPathStatus>(StringComparer.OrdinalIgnoreCase);
        var branch = "(HEAD)";

        foreach (var relative in trackedPaths.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var path = AbsolutePath(root, relative);
            if (path is null) continue;
            tracked.Add(path);
            VisitParents(path, root, parent => trackedFolders.Add(parent));
        }

        var records = porcelain.Split('\0');
        for (var i = 0; i < records.Length; i++)
        {
            var record = records[i];
            if (record.StartsWith("## ", StringComparison.Ordinal))
            {
                branch = ReadBranch(record[3..]);
                continue;
            }
            if (record.Length < 4 || record[2] != ' ')
                continue;

            var x = record[0];
            var y = record[1];
            var file = AbsolutePath(root, record[3..]);
            if (file is null)
                continue;
            var status = Classify(x, y);
            if (status is not null)
            {
                changed[file] = status.Value;
                VisitParents(file, root, parent =>
                {
                    if (!folderChanges.TryGetValue(parent, out var existing))
                        folderChanges[parent] = status.Value;
                    else
                        folderChanges[parent] = Merge(existing, status.Value);
                });
            }

            // With -z, renames/copies contain a second path (the old filename).
            // Retain its parent status in case the file was moved between folders.
            if (x is 'R' or 'C' || y is 'R' or 'C')
            {
                if (++i >= records.Length) break;
                var oldPath = AbsolutePath(root, records[i]);
                if (oldPath is not null && status is { } renameStatus)
                    VisitParents(oldPath, root, parent =>
                    {
                        if (!folderChanges.TryGetValue(parent, out var existing))
                            folderChanges[parent] = renameStatus;
                        else
                            folderChanges[parent] = Merge(existing, renameStatus);
                    });
            }
        }
        return new GitRepositorySnapshot(root, branch, changed, folderChanges, tracked, trackedFolders);
    }

    private static GitPathStatus? Classify(char index, char worktree)
    {
        if (index == 'U' || worktree == 'U' || (index == 'A' && worktree == 'A')
            || (index == 'D' && worktree == 'D'))
            return new(GitFileState.Conflict);
        if (index == '?' && worktree == '?')
            return new(GitFileState.Untracked);
        var staged = index is not (' ' or '?');
        if (worktree != ' ')
            return new(GitFileState.Modified, staged);
        return staged ? new(GitFileState.Staged) : null;
    }

    private static GitPathStatus Merge(GitPathStatus a, GitPathStatus b)
    {
        var priority = static (GitFileState state) => state switch
        {
            GitFileState.Conflict => 5,
            GitFileState.Modified => 4,
            GitFileState.Staged => 3,
            GitFileState.Untracked => 2,
            _ => 1
        };
        return new(priority(a.State) >= priority(b.State) ? a.State : b.State,
            a.AlsoStaged || b.AlsoStaged || a.State == GitFileState.Staged
            || b.State == GitFileState.Staged);
    }

    private static string ReadBranch(string header)
    {
        const string unborn = "No commits yet on ";
        if (header.StartsWith(unborn, StringComparison.Ordinal))
            return header[unborn.Length..];
        if (header.StartsWith("HEAD (", StringComparison.Ordinal))
            return "(분리된 HEAD)";
        var upstream = header.IndexOf("...", StringComparison.Ordinal);
        return upstream < 0 ? header : header[..upstream];
    }

    private static string? AbsolutePath(string root, string relative)
    {
        try
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
                Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))));
            var prefix = Path.EndsInDirectorySeparator(root)
                ? root : root + Path.DirectorySeparatorChar;
            return full.Equals(root, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? full : null;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static void VisitParents(string file, string root, Action<string> visit)
    {
        var parent = Path.GetDirectoryName(file);
        while (parent is not null)
        {
            visit(parent);
            if (parent.Equals(root, StringComparison.OrdinalIgnoreCase))
                break;
            parent = Path.GetDirectoryName(parent);
        }
    }

    private static async Task<string?> RunGitAsync(string root, string[] arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "git",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };
        // No shell interpolation: paths containing spaces/special characters are safe.
        foreach (var option in new[] { "--no-optional-locks", "-c", "core.quotepath=false",
                     "-c", "core.fsmonitor=false", "-C", root })
            process.StartInfo.ArgumentList.Add(option);
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        try
        {
            if (!process.Start())
                return null;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var error = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                await error; // Drain stderr as well, to avoid blocked pipes.
                return process.ExitCode == 0 ? await output : null;
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
                return null;
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException
                                   or IOException or UnauthorizedAccessException)
        {
            // Git is optional: browsing must work even if Git is not installed.
            return null;
        }
    }
}
