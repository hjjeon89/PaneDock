namespace PaneDock.Core.Models;

public sealed record BatchRenameOptions(
    string FindText = "",
    string ReplaceText = "",
    string Prefix = "",
    string Suffix = "",
    bool ChangeExtension = false,
    string Extension = "");

public sealed record RenamePreviewItem(
    string SourcePath, string TargetPath, string? Error = null)
{
    // Detect common external edits/replacements between preview, apply and undo.
    // This metadata guard does not prevent simultaneous filesystem writes.
    public RenameSourceSnapshot? SourceSnapshot { get; init; }
    public string OriginalName => Path.GetFileName(SourcePath);
    public string NewName => Path.GetFileName(TargetPath);
    public string Folder => Path.GetDirectoryName(SourcePath) ?? "";
    public bool Changed => !string.Equals(SourcePath, TargetPath, StringComparison.Ordinal);
    public string Status => Error is not null ? "오류: " + Error
        : Changed ? "변경 예정" : "변경 없음";
}

public sealed record RenameSourceSnapshot(
    long Length, DateTime LastWriteTimeUtc, DateTime CreationTimeUtc);

public sealed record BatchRenamePlan(IReadOnlyList<RenamePreviewItem> Items)
{
    public int ChangeCount => Items.Count(item => item.Changed && item.Error is null);
    public int ErrorCount => Items.Count(item => item.Error is not null);
    public bool CanExecute => ChangeCount > 0 && ErrorCount == 0;
}

public sealed record BatchRenameProgress(int Completed, int Total);

public sealed record BatchRenameExecution(
    bool Success, int ChangedCount, string Message, IReadOnlyList<string> RecoveryPaths)
{
    public static BatchRenameExecution Rejected(string reason) => new(false, 0, reason, []);
}
