using Microsoft.VisualStudio.TestTools.UnitTesting;
using PaneDock.App;
using PaneDock.Core.Models;
using PaneDock.Core.Services;

[TestClass]
public sealed class ExplorerInteractionTests
{
    [TestMethod]
    public void AddressBarCmdRecognizesOnlyKnownCommands()
    {
        Assert.IsTrue(ExplorerAddressCommand.IsCommandPrompt("cmd"));
        Assert.IsTrue(ExplorerAddressCommand.IsCommandPrompt(" CMD.EXE "));
        Assert.IsFalse(ExplorerAddressCommand.IsCommandPrompt("cmd /c del *"));
        Assert.IsFalse(ExplorerAddressCommand.IsCommandPrompt("cmdlet"));
        Assert.IsFalse(ExplorerAddressCommand.IsCommandPrompt(null));
        Assert.AreEqual(@"D:\Work", ExplorerAddressCommand.GetWorkingDirectory(@"D:\Work", @"C:\Users\Test"));
        Assert.AreEqual(@"C:\Users\Test",
            ExplorerAddressCommand.GetWorkingDirectory(ExplorerLocations.ThisPc, @"C:\Users\Test"));
    }

    [TestMethod]
    public void DragCopyRejectsNestedFolderAndInvalidTargetsWithoutDeletingSource()
    {
        var root = CreateTestRoot();
        try
        {
            var sourceFolder = Directory.CreateDirectory(Path.Combine(root, "folder.with.dot"));
            var child = Directory.CreateDirectory(Path.Combine(sourceFolder.FullName, "child"));
            var sibling = Directory.CreateDirectory(Path.Combine(root, "folder.with.dot.sibling"));
            var file = Path.Combine(root, "file-without-extension");
            File.WriteAllText(file, "preserved");

            Assert.IsNull(FileDropCopyPolicy.GetRejectionReason([file], sibling.FullName));
            Assert.IsNull(FileDropCopyPolicy.GetRejectionReason(
                [sourceFolder.FullName], sibling.FullName));
            Assert.IsNotNull(FileDropCopyPolicy.GetRejectionReason(
                [sourceFolder.FullName], sourceFolder.FullName));
            Assert.IsNotNull(FileDropCopyPolicy.GetRejectionReason(
                [sourceFolder.FullName], child.FullName));
            Assert.IsNotNull(FileDropCopyPolicy.GetRejectionReason(
                [file], ExplorerLocations.ThisPc));
            Assert.IsNotNull(FileDropCopyPolicy.GetRejectionReason(
                [file], Path.Combine(root, "missing-destination")));
            Assert.IsNotNull(FileDropCopyPolicy.GetRejectionReason(
                [Path.Combine(root, "missing-file")], sibling.FullName));
            Assert.IsNotNull(FileDropCopyPolicy.GetRejectionReason([], sibling.FullName));
            Assert.AreEqual("preserved", File.ReadAllText(file));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task GlobalSearchWithMultipleRootsSearchesBothAndHonorsOneResultLimit()
    {
        var root = CreateTestRoot();
        try
        {
            var c = Directory.CreateDirectory(Path.Combine(root, "drive-c"));
            var d = Directory.CreateDirectory(Path.Combine(root, "drive-d"));
            File.WriteAllText(Path.Combine(c.FullName, "PaneDock-one.txt"), "c");
            File.WriteAllText(Path.Combine(d.FullName, "panedock-two.txt"), "d");
            var search = new FileSearchService();

            var all = await search.SearchAsync([c.FullName, d.FullName], "PANEDOCK",
                recursive: true, null, CancellationToken.None);
            Assert.AreEqual(2, all.Matched);
            Assert.AreEqual(2, all.Results.Count);
            CollectionAssert.AreEquivalent(
                new[] { c.FullName, d.FullName },
                all.Results.Select(item => item.FolderPath).ToArray());

            var capped = await search.SearchAsync([c.FullName, d.FullName], "panedock",
                recursive: true, null, CancellationToken.None, maxResults: 1);
            Assert.AreEqual(1, capped.Matched);
            Assert.IsTrue(capped.LimitReached);

            var ready = FileSearchService.GetReadyDriveRoots();
            Assert.IsTrue(ready.Count > 0, "현재 PC의 사용 가능한 드라이브를 찾아야 함");
            Assert.IsTrue(ready.All(path => Path.IsPathFullyQualified(path) && Directory.Exists(path)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task FileDropCopyUsesShellCopyAndPreservesSource()
    {
        var root = CreateTestRoot();
        try
        {
            var sourceDirectory = Directory.CreateDirectory(Path.Combine(root, "From"));
            var targetDirectory = Directory.CreateDirectory(Path.Combine(root, "To"));
            var source = Path.Combine(sourceDirectory.FullName, "original.txt");
            var second = Path.Combine(sourceDirectory.FullName, "another.log");
            File.WriteAllText(source, "copy only");
            File.WriteAllText(second, "more");
            var paths = new[] { source, second };

            Assert.IsNull(FileDropCopyPolicy.GetRejectionReason(paths, targetDirectory.FullName));
            var completed = await ShellFileOperation.RunAsync(0, FileCommand.Copy,
                targetDirectory.FullName, paths);

            Assert.IsTrue(completed, "Drop must issue the Shell Copy operation, not Move.");
            Assert.IsTrue(File.Exists(source), "Source file must remain after drag Copy.");
            Assert.IsTrue(File.Exists(second), "Every selected source must remain.");
            Assert.AreEqual("copy only", File.ReadAllText(Path.Combine(targetDirectory.FullName, "original.txt")));
            Assert.AreEqual("more", File.ReadAllText(Path.Combine(targetDirectory.FullName, "another.log")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string CreateTestRoot()
    {
        var folder = Path.Combine(Path.GetTempPath(), "PaneDock-explorer-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }
}
