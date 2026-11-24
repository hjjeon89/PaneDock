using Microsoft.VisualStudio.TestTools.UnitTesting;
using PaneDock.Core.Models;
using PaneDock.Core.Services;

[TestClass]
public sealed class DirectoryListingBatchTests
{
    [TestMethod]
    public async Task LargeDirectoryReportsSmallFirstBatchThenLargerBatches()
    {
        var path = Path.Combine(Path.GetTempPath(),
            "PaneDock-batches-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            for (var i = 0; i < 2000; i++)
                File.WriteAllText(Path.Combine(path, $"item-{i:D4}.tmp"), "");

            var progress = new BatchRecorder();
            await new DirectoryListingService().EnumerateAsync(
                path, progress, CancellationToken.None);

            CollectionAssert.AreEqual(new[] { 256, 512, 1024, 208 },
                progress.BatchSizes);
            Assert.AreEqual(2000, progress.BatchSizes.Sum());
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class BatchRecorder : IProgress<IReadOnlyList<ExplorerEntry>>
    {
        public List<int> BatchSizes { get; } = [];
        public void Report(IReadOnlyList<ExplorerEntry> batch) =>
            BatchSizes.Add(batch.Count);
    }
}
