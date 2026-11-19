using Microsoft.VisualStudio.TestTools.UnitTesting;
using PaneDock.Core.Models;
using PaneDock.Core.Services;

[TestClass]
public sealed class SearchResultTests
{
    [TestMethod]
    public async Task CompletedSearchReturnsEntireResultSetIndependentlyOfProgressCallbacks()
    {
        var folder = Path.Combine(Path.GetTempPath(), "PaneDock-search-results-"
            + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            for (var index = 0; index < 300; index++)
                File.WriteAllText(Path.Combine(folder, $"find-{index:000}.txt"), "");

            var batches = new List<FileSearchResult>();
            var progress = new InlineProgress<FileSearchProgress>(update =>
                batches.AddRange(update.NewItems));
            var service = new FileSearchService();
            var full = await service.SearchAsync(folder, "FIND-", false,
                progress, CancellationToken.None);
            Assert.AreEqual(300, full.Matched);
            Assert.AreEqual(300, full.Results.Count);
            Assert.AreEqual(300, batches.Count);
            Assert.AreEqual(300, full.Results.Select(entry => entry.FullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count());

            var limited = await service.SearchAsync(folder, "find-", false,
                null, CancellationToken.None, maxResults: 200);
            Assert.AreEqual(200, limited.Matched);
            Assert.AreEqual(200, limited.Results.Count);
            Assert.IsTrue(limited.LimitReached);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    private sealed class InlineProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
