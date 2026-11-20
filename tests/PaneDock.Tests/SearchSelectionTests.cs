using Microsoft.VisualStudio.TestTools.UnitTesting;
using PaneDock.Core.Models;
using PaneDock.Core.Services;

[TestClass]
public sealed class SearchSelectionTests
{
    [TestMethod]
    public void MultiSelectionRemovesDuplicateAndNestedDeleteTargets()
    {
        var root = Path.Combine(Path.GetTempPath(), "PaneDock-search-selection");
        var folder = Item(Path.Combine(root, "folder.with.extension"), isDirectory: true);
        var nested = Item(Path.Combine(folder.FullPath, "child.txt"));
        var nestedFolder = Item(Path.Combine(folder.FullPath, "inner"), isDirectory: true);
        var sibling = Item(Path.Combine(root, "folder.with.extension-other", "keep.txt"));

        var targets = SearchResultSelection.DeletionTargets(
            [nested, folder, nestedFolder, sibling, folder]);

        Assert.AreEqual(2, targets.Count);
        Assert.IsTrue(targets.Contains(folder));
        Assert.IsTrue(targets.Contains(sibling));
    }

    [TestMethod]
    public void DeletedFolderPrunesChildrenButPreservesOtherMatchingNames()
    {
        var root = Path.Combine(Path.GetTempPath(), "PaneDock-search-selection");
        var deletedFolder = Item(Path.Combine(root, "tmp"), isDirectory: true);
        var inside = Item(Path.Combine(deletedFolder.FullPath, "nested", "test.tmp"));
        var sibling = Item(Path.Combine(root, "tmp-backup", "keep.tmp"));
        var unrelated = Item(Path.Combine(root, "report.tmp"));

        var results = SearchResultSelection.WithoutDeleted(
            [deletedFolder, inside, sibling, unrelated], [deletedFolder]);

        CollectionAssert.AreEquivalent(new[] { sibling, unrelated }, results.ToArray());
    }

    [TestMethod]
    public void DeleteTargetPlanningRejectsDriveRootAndRelativePaths()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var drive = Item(root, isDirectory: true);
        var invalid = Item("relative-file.txt");
        var safe = Item(Path.Combine(Path.GetTempPath(), "safe-temp.txt"));

        var targets = SearchResultSelection.DeletionTargets([drive, invalid, safe]);

        Assert.AreEqual(1, targets.Count);
        Assert.AreEqual(safe, targets[0]);
    }

    private static FileSearchResult Item(string path, bool isDirectory = false) =>
        new(Path.GetFileName(path), path, Path.GetDirectoryName(path) ?? "",
            isDirectory, null, DateTime.MinValue);
}
