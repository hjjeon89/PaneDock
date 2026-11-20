using Microsoft.VisualStudio.TestTools.UnitTesting;
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
}
