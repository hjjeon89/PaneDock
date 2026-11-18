using PaneDock.Core.Models;

namespace PaneDock.Core.Services;

/// <summary>
/// WPF에 의존하지 않는 점진적 파일 목록 로더.
/// 호출자는 UI 컨텍스트에서 Progress 인스턴스를 만들어야 한다.
/// </summary>
public sealed class DirectoryListingService
{
    private const int BatchSize = 256;

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
