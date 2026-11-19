using PaneDock.Core.Models;

namespace PaneDock.Core.Services;

/// <summary>
/// Safe non-overwriting batch rename. All file sources first move to unique
/// temporary names in the same directory, allowing cycles/case-only changes.
/// On error or cancellation, attempts a two-phase rollback without overwriting
/// third-party files. Temporary locations are reported if recovery fails.
/// </summary>
public sealed class BatchRenameService
{
    public BatchRenamePlan BuildPreview(IEnumerable<string> sources, BatchRenameOptions options)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(options);
        var items = new List<RenamePreviewItem>();
        foreach (var source in sources.Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string full;
            try { full = Path.GetFullPath(source); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException
                                       or PathTooLongException)
            {
                items.Add(new RenamePreviewItem(source, source, "잘못된 원본 경로"));
                continue;
            }
            if (!File.Exists(full))
            {
                items.Add(new RenamePreviewItem(full, full, "파일이 없거나 폴더입니다."));
                continue;
            }

            var sourceName = Path.GetFileName(full);
            var stem = Path.GetFileNameWithoutExtension(sourceName);
            if (!string.IsNullOrEmpty(options.FindText))
                stem = stem.Replace(options.FindText, options.ReplaceText ?? "",
                    StringComparison.OrdinalIgnoreCase);
            stem = (options.Prefix ?? "") + stem + (options.Suffix ?? "");
            var extension = options.ChangeExtension
                ? (options.Extension ?? "").Trim().TrimStart('.')
                : Path.GetExtension(sourceName).TrimStart('.');
            var newName = stem + (extension.Length > 0 ? "." + extension : "");
            if (!WindowsItemName.IsValid(newName))
            {
                items.Add(new RenamePreviewItem(full, full, "허용되지 않는 파일명 또는 확장자"));
                continue;
            }

            var target = Path.Combine(Path.GetDirectoryName(full)!, newName);
            items.Add(new RenamePreviewItem(full, target));
        }

        return ValidateTargets(items);
    }

    public Task<BatchRenameExecution> ExecuteAsync(BatchRenamePlan plan,
        IProgress<BatchRenameProgress>? progress, CancellationToken token) =>
        Task.Run(() => ExecuteCore(plan, progress, token));

    public static BatchRenamePlan CreateUndoPlan(BatchRenamePlan completed) =>
        new(completed.Items.Where(item => item.Changed && item.Error is null)
            .Select(item => new RenamePreviewItem(item.TargetPath, item.SourcePath)).ToArray());

    private static BatchRenamePlan ValidateTargets(IReadOnlyList<RenamePreviewItem> input)
    {
        var items = input.ToArray();
        var vacating = items.Where(item => item.Error is null && item.Changed)
            .Select(item => item.SourcePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targetCount = items.Where(item => item.Error is null)
            .GroupBy(item => item.TargetPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (item.Error is not null) continue;
            string? problem = null;
            if (targetCount[item.TargetPath] > 1)
                problem = "여러 항목의 변경 대상 이름이 중복됩니다.";
            else if (item.Changed
                     && (File.Exists(item.TargetPath) || Directory.Exists(item.TargetPath))
                     && !vacating.Contains(item.TargetPath))
                problem = "동일한 이름의 파일/폴더가 이미 있습니다.";
            if (problem is not null)
                items[i] = item with { Error = problem };
        }
        return new BatchRenamePlan(items);
    }

    private static BatchRenameExecution ExecuteCore(BatchRenamePlan plan,
        IProgress<BatchRenameProgress>? progress, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.CanExecute)
            return BatchRenameExecution.Rejected("오류가 있거나 변경할 파일이 없습니다. 미리보기를 확인하세요.");

        // Repeat ALL checks at execution time to guard against external changes
        // between preview and Apply. Never pass overwrite=true to File.Move.
        var changed = plan.Items.Where(item => item.Changed).ToArray();
        var sources = changed.Select(item => item.SourcePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (changed.Any(item => !File.Exists(item.SourcePath)
            || !Path.IsPathFullyQualified(item.SourcePath)
            || !Path.IsPathFullyQualified(item.TargetPath)
            || !string.Equals(Path.GetDirectoryName(item.SourcePath),
                Path.GetDirectoryName(item.TargetPath), StringComparison.OrdinalIgnoreCase)
            || !WindowsItemName.IsValid(Path.GetFileName(item.TargetPath))))
            return BatchRenameExecution.Rejected("파일이 사라졌거나 허용되지 않는 변경 대상입니다.");
        if (changed.Select(item => item.TargetPath)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != changed.Length)
            return BatchRenameExecution.Rejected("변경 대상 이름이 중복됩니다.");
        if (changed.Any(item => (File.Exists(item.TargetPath) || Directory.Exists(item.TargetPath))
            && !sources.Contains(item.TargetPath)))
            return BatchRenameExecution.Rejected("미리보기 이후 새 파일이 생겨 이름이 충돌했습니다.");

        var stages = new List<StagedRename>(changed.Length);
        var done = 0;
        try
        {
            foreach (var item in changed)
            {
                token.ThrowIfCancellationRequested();
                var temp = NewTemporaryPath(item.SourcePath);
                File.Move(item.SourcePath, temp);
                stages.Add(new StagedRename(item.SourcePath, item.TargetPath, temp));
            }
            foreach (var stage in stages)
            {
                token.ThrowIfCancellationRequested();
                File.Move(stage.CurrentPath, stage.TargetPath);
                stage.CurrentPath = stage.TargetPath;
                done++;
                progress?.Report(new BatchRenameProgress(done, changed.Length));
            }
            return new BatchRenameExecution(true, done, $"{done}개 파일 변경 완료", []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or OperationCanceledException)
        {
            // A committed target may be the original name of another file.
            // First move all committed targets aside, then restore every source.
            foreach (var stage in stages.Where(item =>
                         string.Equals(item.CurrentPath, item.TargetPath, StringComparison.Ordinal)))
            {
                try
                {
                    var rollbackTemp = NewTemporaryPath(stage.SourcePath);
                    File.Move(stage.CurrentPath, rollbackTemp);
                    stage.CurrentPath = rollbackTemp;
                }
                catch (Exception rollbackError) when (rollbackError is IOException or UnauthorizedAccessException)
                {
                    // The next rollback pass may still restore it directly.
                }
            }
            foreach (var stage in stages.Where(item =>
                         !string.Equals(item.CurrentPath, item.SourcePath, StringComparison.Ordinal)))
            {
                try
                {
                    File.Move(stage.CurrentPath, stage.SourcePath);
                    stage.CurrentPath = stage.SourcePath;
                }
                catch (Exception rollbackError) when (rollbackError is IOException or UnauthorizedAccessException)
                {
                    // Preserve the current path for explicit manual recovery.
                }
            }
            var failedRecovery = stages.Where(item =>
                    !string.Equals(item.CurrentPath, item.SourcePath, StringComparison.Ordinal))
                .Select(item => item.CurrentPath)
                .ToArray();
            var reason = ex is OperationCanceledException
                ? "변경 취소" : "변경 실패: " + ex.Message;
            if (failedRecovery.Length > 0)
                reason += " · 자동 복원에 실패한 파일 경로를 확인하세요.";
            else
                reason += " · 변경한 파일을 원래 이름으로 복구했습니다.";
            return new BatchRenameExecution(false, 0, reason,
                failedRecovery.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }
    }

    private static string NewTemporaryPath(string source)
    {
        var folder = Path.GetDirectoryName(source)!;
        for (var n = 0; n < 10; n++)
        {
            var candidate = Path.Combine(folder, ".panedock-" +
                Guid.NewGuid().ToString("N") + ".tmp");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }
        throw new IOException("임시 작업 파일명을 확보하지 못했습니다.");
    }

    private sealed class StagedRename(string source, string target, string current)
    {
        public string SourcePath { get; } = source;
        public string TargetPath { get; } = target;
        public string CurrentPath { get; set; } = current;
    }
}
