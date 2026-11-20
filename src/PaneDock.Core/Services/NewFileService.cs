namespace PaneDock.Core.Services;

/// <summary>Creates an empty file atomically, without overwriting existing data.</summary>
public sealed class NewFileService
{
    public Task<string> CreateEmptyAsync(string directory, string fileName,
        CancellationToken cancellationToken = default)
    {
        if (!WindowsItemName.IsValid(fileName))
            throw new ArgumentException("Windows에서 허용되지 않는 파일 이름입니다.", nameof(fileName));
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(directory))
                throw new DirectoryNotFoundException($"대상 폴더를 찾을 수 없습니다: {directory}");
            var target = Path.Combine(Path.GetFullPath(directory), fileName);
            using (new FileStream(target, FileMode.CreateNew, FileAccess.Write,
                       FileShare.Read, bufferSize: 1, FileOptions.None))
            {
                // Intentionally create a zero-byte file; never open existing files.
            }
            return target;
        }, cancellationToken);
    }
}
