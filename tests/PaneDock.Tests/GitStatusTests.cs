using System.ComponentModel;
using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PaneDock.Core.Models;
using PaneDock.Core.Services;

[TestClass]
public sealed class GitStatusTests
{
    [TestMethod]
    public void PorcelainStatusDistinguishesChangesStagingConflictsAndCleanEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), "PaneDock-git-model");
        var tracked = string.Join('\0', new[]
        {
            "src/clean.cs", "src/edit.cs", "src/staged.cs",
            "src/conflict.cs", "src/both.cs", "old/previous.cs", ""
        });
        var status = "## feature/status...origin/feature/status\0"
            + " M src/edit.cs\0"
            + "M  src/staged.cs\0"
            + "MM src/both.cs\0"
            + "UU src/conflict.cs\0"
            + "?? src/new file.cs\0"
            + "R  renamed/current.cs\0old/previous.cs\0";

        var snapshot = GitStatusService.Parse(root, status, tracked);
        Assert.AreEqual("feature/status", snapshot.Branch);
        Assert.AreEqual(GitFileState.Clean,
            snapshot.GetStatus(Path.Combine(root, "src", "clean.cs"), false)?.State);
        Assert.AreEqual(GitFileState.Modified,
            snapshot.GetStatus(Path.Combine(root, "src", "edit.cs"), false)?.State);
        Assert.AreEqual(GitFileState.Staged,
            snapshot.GetStatus(Path.Combine(root, "src", "staged.cs"), false)?.State);
        var both = snapshot.GetStatus(Path.Combine(root, "src", "both.cs"), false);
        Assert.AreEqual(GitFileState.Modified, both?.State);
        Assert.IsTrue(both?.AlsoStaged);
        Assert.AreEqual(GitFileState.Conflict,
            snapshot.GetStatus(Path.Combine(root, "src", "conflict.cs"), false)?.State);
        Assert.AreEqual(GitFileState.Untracked,
            snapshot.GetStatus(Path.Combine(root, "src", "new file.cs"), false)?.State);
        Assert.AreEqual(GitFileState.Staged,
            snapshot.GetStatus(Path.Combine(root, "renamed"), true)?.State);
        Assert.AreEqual(GitFileState.Staged,
            snapshot.GetStatus(Path.Combine(root, "old"), true)?.State);
        Assert.AreEqual(GitFileState.Conflict,
            snapshot.GetStatus(Path.Combine(root, "src"), true)?.State);
        Assert.AreEqual(GitFileState.Conflict, snapshot.GetStatus(root, true)?.State);
        Assert.IsNull(snapshot.GetStatus(Path.Combine(root, "src", "ignored.tmp"), false));
        Assert.IsNull(snapshot.GetStatus(Path.Combine(root, "untracked-empty-folder"), true));
    }

    [TestMethod]
    public void DetectsGitWorktreeMarkerFileAndDoesNotGuessOutsideRepository()
    {
        var root = Path.Combine(Path.GetTempPath(), "PaneDock-git-root-" + Guid.NewGuid().ToString("N"));
        try
        {
            var nested = Directory.CreateDirectory(Path.Combine(root, "sub", "child"));
            File.WriteAllText(Path.Combine(root, ".git"), "gitdir: elsewhere");
            Assert.AreEqual(root, GitStatusService.FindRepositoryRoot(nested.FullName));
            Assert.AreEqual(root, GitStatusService.FindRepositoryRoot(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task RealGitReportsUntrackedModifiedStagedAndCommittedFiles()
    {
        if (!CanRunGit())
            return; // PaneDock also runs normally on systems without Git.

        var root = Path.Combine(Path.GetTempPath(),
            "PaneDock-git-status-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            RunGit(root, "init", "-q");
            var clean = Path.Combine(root, "clean.txt");
            var changed = Path.Combine(root, "changed.txt");
            var staged = Path.Combine(root, "staged.txt");
            var untracked = Path.Combine(root, "한글 file.txt");
            var ignored = Path.Combine(root, "secret.tmp");
            var nested = Directory.CreateDirectory(Path.Combine(root, "nested"));
            File.WriteAllText(clean, "base");
            File.WriteAllText(changed, "base");
            File.WriteAllText(staged, "base");
            File.WriteAllText(Path.Combine(nested.FullName, "tracked.txt"), "base");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "*.tmp\n");
            RunGit(root, "add", ".");
            RunGit(root, "-c", "user.name=PaneDock Test", "-c",
                "user.email=panedock@example.invalid", "commit", "-qm", "base");

            File.AppendAllText(changed, "-edited");
            File.AppendAllText(staged, "-staged");
            RunGit(root, "add", "--", "staged.txt");
            File.WriteAllText(untracked, "new");
            File.WriteAllText(ignored, "ignored");

            var service = new GitStatusService();
            var snapshot = await service.GetSnapshotAsync(nested.FullName);
            Assert.IsNotNull(snapshot);
            Assert.AreEqual(root, snapshot.Root);
            Assert.AreEqual(GitFileState.Clean, snapshot.GetStatus(clean, false)?.State);
            Assert.AreEqual(GitFileState.Modified, snapshot.GetStatus(changed, false)?.State);
            Assert.AreEqual(GitFileState.Staged, snapshot.GetStatus(staged, false)?.State);
            Assert.AreEqual(GitFileState.Untracked, snapshot.GetStatus(untracked, false)?.State);
            Assert.IsNull(snapshot.GetStatus(ignored, false), "Git ignored files must not be decorated.");
            Assert.AreEqual(GitFileState.Clean,
                snapshot.GetStatus(nested.FullName, true)?.State);
            Assert.AreEqual(GitFileState.Modified, snapshot.GetStatus(root, true)?.State);

            RunGit(root, "add", "--", "changed.txt", "한글 file.txt");
            RunGit(root, "-c", "user.name=PaneDock Test", "-c",
                "user.email=panedock@example.invalid", "commit", "-qm", "changes");
            var afterCommit = await service.GetSnapshotAsync(root, force: true);
            Assert.IsNotNull(afterCommit);
            Assert.AreEqual(GitFileState.Clean, afterCommit.GetStatus(changed, false)?.State);
            Assert.AreEqual(GitFileState.Clean, afterCommit.GetStatus(untracked, false)?.State);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [TestMethod]
    public async Task RealGitMergeConflictAppearsOnFileAndParentFolder()
    {
        if (!CanRunGit())
            return;

        var root = Path.Combine(Path.GetTempPath(),
            "PaneDock-git-conflict-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            RunGit(root, "init", "-q");
            var baseBranch = RunGitResult(root, "branch", "--show-current").Output.Trim();
            var file = Path.Combine(root, "src", "shared.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "original\n");
            RunGit(root, "add", ".");
            Commit(root, "original");

            RunGit(root, "checkout", "-qb", "topic");
            File.WriteAllText(file, "topic branch\n");
            RunGit(root, "add", ".");
            Commit(root, "topic");

            RunGit(root, "checkout", "-q", baseBranch);
            File.WriteAllText(file, "current branch\n");
            RunGit(root, "add", ".");
            Commit(root, "current");

            var merge = RunGitResult(root, "merge", "--no-edit", "topic");
            Assert.AreNotEqual(0, merge.Code, "The test must produce a merge conflict.");

            var snapshot = await new GitStatusService().GetSnapshotAsync(root);
            Assert.IsNotNull(snapshot);
            Assert.AreEqual(GitFileState.Conflict, snapshot.GetStatus(file, false)?.State);
            Assert.AreEqual(GitFileState.Conflict,
                snapshot.GetStatus(Path.GetDirectoryName(file)!, true)?.State);
            Assert.AreEqual(GitFileState.Conflict, snapshot.GetStatus(root, true)?.State);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    private static bool CanRunGit()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("git", "--version")
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true
                }
            };
            if (!process.Start()) return false;
            return process.WaitForExit(5000) && process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private static void RunGit(string root, params string[] args)
    {
        var result = RunGitResult(root, args);
        Assert.AreEqual(0, result.Code,
            $"git {string.Join(' ', args)} failed: {result.Output} {result.Error}");
    }

    private static void Commit(string root, string message) =>
        RunGit(root, "-c", "user.name=PaneDock Test", "-c",
            "user.email=panedock@example.invalid", "commit", "-qm", message);

    private static (int Code, string Output, string Error) RunGitResult(string root, params string[] args)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "git",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = root
        };
        foreach (var arg in args)
            process.StartInfo.ArgumentList.Add(arg);
        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.IsTrue(process.WaitForExit(10000),
            $"git {string.Join(' ', args)} timed out.");
        return (process.ExitCode, output, error);
    }

    private static void DeleteTestRepository(string root)
    {
        if (!Directory.Exists(root))
            return;

        // Git creates read-only object files on Windows.
        foreach (var file in Directory.EnumerateFiles(root, "*", new EnumerationOptions
                 {
                     RecurseSubdirectories = true, AttributesToSkip = 0
                 }))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(root, recursive: true);
    }
}
