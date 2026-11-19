namespace PaneDock.Core.Models;

public sealed record FileSearchResult(
    string Name, string FullPath, string FolderPath, bool IsDirectory, long? Size,
    DateTime ModifiedAt)
{
    public string Type => IsDirectory ? "폴더" : "파일";
    public string SizeText => Size is null ? "—" : $"{Size.Value:N0} B";
    public string ModifiedText => ModifiedAt.ToString("yyyy-MM-dd HH:mm");
}

public sealed record FileSearchProgress(
    IReadOnlyList<FileSearchResult> NewItems, int Matched, int DirectoriesScanned,
    int SkippedFolders, bool LimitReached);

public sealed record FileSearchSummary(
    int Matched, int DirectoriesScanned, int SkippedFolders, bool LimitReached);
