using PaneDock.Core.Models;

namespace PaneDock.Core.Services;

/// <summary>
/// WPF에 의존하지 않는 점진적 파일 목록 로더.
/// 호출자는 UI 컨텍스트에서 Progress 인스턴스를 만들어야 한다.
/// </summary>
public sealed class DirectoryListingService
{
    private const int BatchSize = 256;

    /// <summary>
    /// Captures an entire directory off the UI thread. Used for change diffs
    /// so the visible list and selection need not be cleared on every event.
    /// </summary>
    public Task<IReadOnlyList<ExplorerEntry>> GetSnapshotAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Task.Run<IReadOnlyList<ExplorerEntry>>(() =>
        {
            if (ExplorerLocations.IsThisPc(path))
                return GetDriveEntries(cancellationToken);

            var results = new List<ExplorerEntry>();
            foreach (var info in new DirectoryInfo(path).EnumerateFileSystemInfos())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TryCreateEntry(info) is { } entry)
                    results.Add(entry);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return results;
        }, cancellationToken);
    }

    private static ExplorerEntry[] GetDriveEntries(CancellationToken token) =>
        DriveInfo.GetDrives()
            .OrderBy(drive => drive.Name, StringComparer.OrdinalIgnoreCase)
            .Select(drive =>
            {
                token.ThrowIfCancellationRequested();
                return new ExplorerEntry(drive.Name, drive.Name, "", null,
                    DateTime.MinValue, true, IsDrive: true);
            }).ToArray();

    public Task<DirectoryInfo[]> GetFoldersAsync(string path, bool showHidden) =>
        Task.Run(() => new DirectoryInfo(path).EnumerateDirectories("*", new EnumerationOptions
        {
            IgnoreInaccessible = false,
            AttributesToSkip = showHidden ? 0 : FileAttributes.Hidden | FileAttributes.System
        }).OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase).ToArray());

    public Task EnumerateAsync(
        string path,
        IProgress<IReadOnlyList<ExplorerEntry>> progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(progress);

        return Task.Run(() =>
        {
            if (ExplorerLocations.IsThisPc(path))
            {
                var drives = GetDriveEntries(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                progress.Report(drives);
                return;
            }

            var batch = new List<ExplorerEntry>(BatchSize);

            foreach (var info in new DirectoryInfo(path).EnumerateFileSystemInfos())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = TryCreateEntry(info);
                if (item is null)
                    continue;

                batch.Add(item);
                if (batch.Count < BatchSize)
                    continue;

                progress.Report(batch.ToArray());
                batch.Clear();
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (batch.Count > 0)
                progress.Report(batch.ToArray());
        }, cancellationToken);
    }

    private static ExplorerEntry? TryCreateEntry(FileSystemInfo info)
    {
        try
        {
            var attributes = info.Attributes;
            var isDirectory = (attributes & FileAttributes.Directory) != 0;
            return new ExplorerEntry(
                info.FullName,
                info.Name,
                isDirectory ? string.Empty : info.Extension,
                info is FileInfo file ? file.Length : null,
                info.LastWriteTime,
                isDirectory,
                (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0,
                info.CreationTime);
        }
        catch (IOException)
        {
            return null; // 목록을 가져오는 동안 사라진 항목
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
