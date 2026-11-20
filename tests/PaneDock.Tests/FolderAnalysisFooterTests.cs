using Microsoft.VisualStudio.TestTools.UnitTesting;
using PaneDock.Core.Models;
using PaneDock.Core.Services;

[TestClass]
public sealed class FolderAnalysisFooterTests
{
    [TestMethod]
    public async Task SelectedFoldersReturnIndividualAndAggregateCountsForFooterAndPopup()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "PaneDock-footer-analysis-" + Guid.NewGuid().ToString("N"));
        try
        {
            var alpha = Directory.CreateDirectory(Path.Combine(root, "FolderAlpha"));
            var nested = Directory.CreateDirectory(Path.Combine(alpha.FullName, "Nested"));
            var beta = Directory.CreateDirectory(Path.Combine(root, "FolderBeta"));
            File.WriteAllBytes(Path.Combine(alpha.FullName, "first.bin"), new byte[3]);
            File.WriteAllBytes(Path.Combine(nested.FullName, "second.bin"), new byte[5]);
            File.WriteAllBytes(Path.Combine(beta.FullName, "third.bin"), new byte[7]);

            var report = await new FolderAnalysisService().AnalyzeAsync(
                [alpha.FullName, beta.FullName], null, CancellationToken.None);

            Assert.AreEqual(2, report.Roots.Count);
            Assert.AreEqual(3L, report.TotalFiles);
            Assert.AreEqual(1L, report.TotalSubfolders);
            Assert.AreEqual(15L, report.TotalBytes);
            Assert.IsTrue(report.Roots.Any(item =>
                item.Path == alpha.FullName && item.FileCount == 2
                && item.SubfolderCount == 1 && item.TotalBytes == 8));
            Assert.IsTrue(report.Roots.Any(item =>
                item.Path == beta.FullName && item.FileCount == 1
                && item.SubfolderCount == 0 && item.TotalBytes == 7));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task UiAnalysisWithCanceledTokenReturnsNoResult()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await new FolderAnalysisService().AnalyzeForUiAsync(
            [Path.GetTempPath()], null, cancellation.Token);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task UiAnalysisCanceledAfterFirstFolderSkipsRemainingFolders()
    {
        var root = Path.Combine(Path.GetTempPath(),
            "PaneDock-cancel-analysis-" + Guid.NewGuid().ToString("N"));
        try
        {
            var first = Directory.CreateDirectory(Path.Combine(root, "First"));
            var second = Directory.CreateDirectory(Path.Combine(root, "Second"));
            File.WriteAllText(Path.Combine(first.FullName, "one.txt"), "one");
            File.WriteAllText(Path.Combine(second.FullName, "two.txt"), "two");
            using var cancellation = new CancellationTokenSource();
            var progress = new ImmediateProgress<FolderAnalysisProgress>(update =>
            {
                if (update.CompletedRoot is not null)
                    cancellation.Cancel();
            });

            var result = await new FolderAnalysisService().AnalyzeForUiAsync(
                [first.FullName, second.FullName], progress, cancellation.Token);

            Assert.IsNull(result, "Canceled analysis must not present partial totals.");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
