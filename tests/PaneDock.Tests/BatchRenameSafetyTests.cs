using Microsoft.VisualStudio.TestTools.UnitTesting;
using PaneDock.Core.Models;
using PaneDock.Core.Services;

[TestClass]
public sealed class BatchRenameSafetyTests
{
    [TestMethod]
    public async Task PartialFailureRestoresAllOriginalNamesWithoutOverwritingNewFile()
    {
        var folder = NewTestFolder();
        try
        {
            var first = Path.Combine(folder, "one.txt");
            var second = Path.Combine(folder, "two.txt");
            var intruder = Path.Combine(folder, "renamed_two.txt");
            File.WriteAllText(first, "one");
            File.WriteAllText(second, "two");
            var service = new BatchRenameService();
            var plan = service.BuildPreview([first, second],
                new BatchRenameOptions(Prefix: "renamed_"));
            Assert.IsTrue(plan.CanExecute);

            var report = new InlineProgress<BatchRenameProgress>(step =>
            {
                if (step.Completed == 1)
                    File.WriteAllText(intruder, "externally created file");
            });
            var result = await service.ExecuteAsync(plan, report, CancellationToken.None);

            Assert.IsFalse(result.Success, "New conflicting file must fail the second rename.");
            Assert.AreEqual(0, result.RecoveryPaths.Count, result.Message);
            Assert.AreEqual("one", File.ReadAllText(first));
            Assert.AreEqual("two", File.ReadAllText(second));
            Assert.AreEqual("externally created file", File.ReadAllText(intruder));
            Assert.IsFalse(File.Exists(Path.Combine(folder, "renamed_one.txt")));
            Assert.IsFalse(Directory.EnumerateFiles(folder, ".panedock-*.tmp").Any());
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [TestMethod]
    public async Task EditedSourceBetweenPreviewAndApplyIsRejected()
    {
        var folder = NewTestFolder();
        try
        {
            var original = Path.Combine(folder, "report.txt");
            File.WriteAllText(original, "original");
            var service = new BatchRenameService();
            var plan = service.BuildPreview([original], new BatchRenameOptions(Prefix: "new_"));
            Assert.IsTrue(plan.CanExecute);
            File.WriteAllText(original, "replaced contents with different length");

            var result = await service.ExecuteAsync(plan, null, CancellationToken.None);
            Assert.IsFalse(result.Success);
            Assert.AreEqual("replaced contents with different length", File.ReadAllText(original));
            Assert.IsFalse(File.Exists(Path.Combine(folder, "new_report.txt")));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [TestMethod]
    public async Task UndoDoesNotRenameAReplacedFile()
    {
        var folder = NewTestFolder();
        try
        {
            var original = Path.Combine(folder, "report.txt");
            var renamed = Path.Combine(folder, "new_report.txt");
            File.WriteAllText(original, "original");
            var service = new BatchRenameService();
            var plan = service.BuildPreview([original], new BatchRenameOptions(Prefix: "new_"));
            Assert.IsTrue((await service.ExecuteAsync(plan, null, CancellationToken.None)).Success);

            File.Delete(renamed);
            File.WriteAllText(renamed, "this is another file");
            var result = await service.ExecuteAsync(
                BatchRenameService.CreateUndoPlan(plan), null, CancellationToken.None);
            Assert.IsFalse(result.Success);
            Assert.IsFalse(File.Exists(original));
            Assert.AreEqual("this is another file", File.ReadAllText(renamed));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [TestMethod]
    public async Task CancellationDuringCommitRestoresOriginalNames()
    {
        var folder = NewTestFolder();
        try
        {
            var first = Path.Combine(folder, "a.txt");
            var second = Path.Combine(folder, "b.txt");
            File.WriteAllText(first, "a");
            File.WriteAllText(second, "b");
            var service = new BatchRenameService();
            var plan = service.BuildPreview([first, second], new BatchRenameOptions(Prefix: "new_"));
            using var cancellation = new CancellationTokenSource();
            var progress = new InlineProgress<BatchRenameProgress>(step =>
            {
                if (step.Completed == 1) cancellation.Cancel();
            });
            var result = await service.ExecuteAsync(plan, progress, cancellation.Token);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(0, result.RecoveryPaths.Count, result.Message);
            Assert.AreEqual("a", File.ReadAllText(first));
            Assert.AreEqual("b", File.ReadAllText(second));
            Assert.IsFalse(Directory.EnumerateFiles(folder, ".panedock-*.tmp").Any());
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    private static string NewTestFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "PaneDock-safety-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class InlineProgress<T>(Action<T> onReport) : IProgress<T>
    {
        public void Report(T value) => onReport(value);
    }
}
