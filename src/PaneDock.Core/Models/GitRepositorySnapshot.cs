namespace PaneDock.Core.Models;

public enum GitFileState
{
    Clean,
    Untracked,
    Staged,
    Modified,
    Conflict
}

public readonly record struct GitPathStatus(GitFileState State, bool AlsoStaged = false);

/// <summary>
/// Immutable Git information for a repository. The keys are absolute paths so
/// multiple explorer panes can share one snapshot without changing their rows.
/// </summary>
public sealed class GitRepositorySnapshot
{
    private readonly IReadOnlyDictionary<string, GitPathStatus> _changedPaths;
    private readonly IReadOnlyDictionary<string, GitPathStatus> _changedFolders;
    private readonly IReadOnlySet<string> _trackedFiles;
    private readonly IReadOnlySet<string> _trackedFolders;

    internal GitRepositorySnapshot(string root, string branch,
        IReadOnlyDictionary<string, GitPathStatus> changedPaths,
        IReadOnlyDictionary<string, GitPathStatus> changedFolders,
        IReadOnlySet<string> trackedFiles, IReadOnlySet<string> trackedFolders)
    {
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        Branch = branch;
        _changedPaths = changedPaths;
        _changedFolders = changedFolders;
        _trackedFiles = trackedFiles;
        _trackedFolders = trackedFolders;
    }

    public string Root { get; }
    public string Branch { get; }

    public GitPathStatus? GetStatus(string path, bool isDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        // Paths come from the explorer's filesystem listing.
        var fullPath = Path.TrimEndingDirectorySeparator(path);
        if (_changedPaths.TryGetValue(fullPath, out var changed))
            return changed;
        if (isDirectory && _changedFolders.TryGetValue(fullPath, out changed))
            return changed;

        if (isDirectory ? _trackedFolders.Contains(fullPath) || _trackedFiles.Contains(fullPath)
                        : _trackedFiles.Contains(fullPath))
            return new GitPathStatus(GitFileState.Clean);

        return null; // Ignored paths and folders outside a Git repository.
    }
}
