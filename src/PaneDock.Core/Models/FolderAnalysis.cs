namespace PaneDock.Core.Models;

public sealed record FolderAnalysisItem(
    string Path, long FileCount, long SubfolderCount, long TotalBytes,
    long SkippedFolders, string? Error = null)
{
    public string SizeText => FormatSize(TotalBytes);

    public static string FormatSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB", "PB"];
        double number = bytes;
        var index = 0;
        while (number >= 1024 && index < sizes.Length - 1)
        {
            number /= 1024;
            index++;
        }
        return $"{number:0.##} {sizes[index]}";
    }
}

public sealed record FolderAnalysisProgress(
    string CurrentFolder, long Files, long Subfolders, long Bytes,
    FolderAnalysisItem? CompletedRoot = null);

public sealed record FolderAnalysisReport(
    IReadOnlyList<FolderAnalysisItem> Roots, int OverlappingRootsSkipped)
{
    public long TotalFiles => Roots.Sum(root => root.FileCount);
    public long TotalSubfolders => Roots.Sum(root => root.SubfolderCount);
    public long TotalBytes => Roots.Sum(root => root.TotalBytes);
    public long SkippedFolders => Roots.Sum(root => root.SkippedFolders);
}
