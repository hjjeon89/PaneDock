using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PaneDock.Core.Services;

[TestClass]
public sealed class EverythingIndexedSearchTests
{
    [TestMethod]
    public void IndexSearchQuerySupportsAllFilesAndScopedFolders()
    {
        Assert.AreEqual("report",
            EverythingIndexedSearchService.BuildQuery(" report ", null, recursive: true));
        var root = Path.Combine(Path.GetTempPath(), "PaneDock example");
        Assert.AreEqual("<report> \"" + root + Path.DirectorySeparatorChar + "\"",
            EverythingIndexedSearchService.BuildQuery("report", root, recursive: true));
        Assert.AreEqual("<report> parent:\"" + root + "\"",
            EverythingIndexedSearchService.BuildQuery("report", root, recursive: false));
    }

    [TestMethod]
    public async Task InstalledEverythingIndexReturnsMatchingResults()
    {
        var engine = new EverythingIndexedSearchService();
        if (!engine.IsAvailable)
            return; // Optional dependency is absent on CI or a fresh Windows installation.

        var stopwatch = Stopwatch.StartNew();
        var result = await engine.TrySearchAsync("PaneDock", null, true, 250,
            CancellationToken.None);
        Assert.IsNotNull(result, "Everything is running but its index/IPC could not be queried.");
        Assert.IsTrue(result.Results.Count > 0,
            "The local PaneDock repository should be present in the Everything index.");
        Assert.IsTrue(result.Results.All(item => !string.IsNullOrWhiteSpace(item.FullPath)));
        Console.WriteLine($"Everything indexed query returned {result.Results.Count} rows in {stopwatch.ElapsedMilliseconds}ms");
    }

    [TestMethod]
    public async Task IndexedSearchRestrictsToExactParentWhenRecursiveIsOff()
    {
        var engine = new EverythingIndexedSearchService();
        if (!engine.IsAvailable) return;
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PaneDock.sln")))
            directory = directory.Parent;
        Assert.IsNotNull(directory, "Repository root could not be located from the test output.");

        var report = await engine.TrySearchAsync("PaneDock.sln",
            directory.FullName, recursive: false, maxResults: 50, CancellationToken.None);
        Assert.IsNotNull(report);
        // Everything only indexes configured volumes/folders. If this build's
        // source drive is not indexed, a zero result is not an SDK error.
        Assert.IsTrue(report.Results.All(item => string.Equals(item.FolderPath,
            directory.FullName, StringComparison.OrdinalIgnoreCase)),
            "Exact parent scope must never return entries from subfolders.");
    }
}
