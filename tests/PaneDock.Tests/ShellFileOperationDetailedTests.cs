using Microsoft.VisualStudio.TestTools.UnitTesting;
using PaneDock.App;

[TestClass]
public sealed class ShellFileOperationDetailedTests
{
    [TestMethod]
    public async Task ShellReportsItemLevelResultsForRenameCopyAndMove()
    {
        var root = CreateRoot();
        try
        {
            var source = Directory.CreateDirectory(Path.Combine(root, "Source"));
            var destination = Directory.CreateDirectory(Path.Combine(root, "Target"));
            var moveDestination = Directory.CreateDirectory(Path.Combine(root, "Moved"));
            var first = Path.Combine(source.FullName, "one.txt");
            File.WriteAllText(first, "one");

            var rename = await ShellFileOperation.RunDetailedAsync(
                0, FileCommand.Rename, source.FullName, [first], "renamed.txt");
            Console.WriteLine("RENAME: " + rename);
            Assert.IsTrue(File.Exists(Path.Combine(source.FullName, "renamed.txt")));
            Assert.IsTrue(rename.Succeeded, rename.ToString());

            var renamed = Path.Combine(source.FullName, "renamed.txt");
            var second = Path.Combine(source.FullName, "two.txt");
            var third = Path.Combine(source.FullName, "three.txt");
            File.WriteAllText(second, "two");
            File.WriteAllText(third, "three");
            var copy = await ShellFileOperation.RunDetailedAsync(
                0, FileCommand.Copy, destination.FullName, [renamed, second, third]);
            Console.WriteLine("COPY: " + copy);
            Assert.AreEqual(3, copy.CompletedItems);
            Assert.IsTrue(copy.Succeeded, copy.ToString());

            var moved = await ShellFileOperation.RunDetailedAsync(
                0, FileCommand.Cut, moveDestination.FullName, [renamed, second, third]);
            Console.WriteLine("MOVE: " + moved);
            Assert.AreEqual(3, moved.CompletedItems);
            Assert.IsTrue(moved.Succeeded, moved.ToString());
            Assert.AreEqual(0, Directory.GetFiles(source.FullName).Length);
            Assert.AreEqual(3, Directory.GetFiles(moveDestination.FullName).Length);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task CancellationBeforeShellStartsPreservesAllSources()
    {
        var root = CreateRoot();
        try
        {
            var source = Path.Combine(root, "safe.txt");
            var destination = Directory.CreateDirectory(Path.Combine(root, "Target"));
            File.WriteAllText(source, "original");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var result = await ShellFileOperation.RunDetailedAsync(
                0, FileCommand.Copy, destination.FullName, [source],
                cancellation: cancellation.Token);
            Assert.AreEqual(0, result.CompletedItems);
            Assert.IsTrue(result.WasCanceled);
            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(File.Exists(source));
            Assert.IsFalse(File.Exists(Path.Combine(destination.FullName, "safe.txt")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task CancelDeleteBeforeStartingLeavesTemporaryFilesUntouched()
    {
        var root = CreateRoot();
        try
        {
            var paths = Enumerable.Range(1, 3)
                .Select(index => Path.Combine(root, $"delete-{index}.txt")).ToArray();
            foreach (var path in paths)
                File.WriteAllText(path, "must survive");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            var recycle = await ShellFileOperation.RunDetailedAsync(
                0, FileCommand.Delete, root, paths, cancellation: cancellation.Token);
            var permanent = await ShellFileOperation.RunDetailedAsync(
                0, FileCommand.DeletePermanently, root, paths, cancellation: cancellation.Token);

            Assert.IsTrue(recycle.WasCanceled);
            Assert.IsTrue(permanent.WasCanceled);
            Assert.AreEqual(0, recycle.CompletedItems);
            Assert.AreEqual(0, permanent.CompletedItems);
            Assert.IsTrue(paths.All(File.Exists), "Canceled delete must not touch source files.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task CancelCopyAfterFirstCompletedItemLeavesSourcesAndReportsPartial()
    {
        var root = CreateRoot();
        try
        {
            var source = Directory.CreateDirectory(Path.Combine(root, "Source"));
            var destination = Directory.CreateDirectory(Path.Combine(root, "Target"));
            var paths = Enumerable.Range(1, 4).Select(n =>
                Path.Combine(source.FullName, $"source-{n}.txt")).ToArray();
            foreach (var path in paths)
                File.WriteAllText(path, Path.GetFileName(path));

            using var cancellation = new CancellationTokenSource();
            var result = await ShellFileOperation.RunDetailedAsync(
                0, FileCommand.Copy, destination.FullName, paths,
                cancellation: cancellation.Token,
                onItemCompleted: count =>
                {
                    if (count == 1) cancellation.Cancel();
                });

            Console.WriteLine("CANCELED COPY: " + result);
            Assert.IsTrue(result.WasCanceled || result.WasAborted,
                "A pending copy should stop after cancellation is requested.");
            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.CompletedItems >= 1 && result.CompletedItems < paths.Length,
                result.ToString());
            Assert.IsTrue(paths.All(File.Exists), "Copy must preserve every source.");
            Assert.AreEqual(result.CompletedItems,
                Directory.EnumerateFiles(destination.FullName).Count());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task CancelMoveAfterFirstCompletedItemReportsPartialWithoutLosingFiles()
    {
        var root = CreateRoot();
        try
        {
            var source = Directory.CreateDirectory(Path.Combine(root, "Source"));
            var destination = Directory.CreateDirectory(Path.Combine(root, "Target"));
            var paths = Enumerable.Range(1, 4).Select(n =>
                Path.Combine(source.FullName, $"move-{n}.txt")).ToArray();
            foreach (var path in paths)
                File.WriteAllText(path, Path.GetFileName(path));

            using var cancellation = new CancellationTokenSource();
            var result = await ShellFileOperation.RunDetailedAsync(
                0, FileCommand.Cut, destination.FullName, paths,
                cancellation: cancellation.Token,
                onItemCompleted: count =>
                {
                    if (count == 1) cancellation.Cancel();
                });

            Console.WriteLine("CANCELED MOVE: " + result);
            Assert.IsTrue(result.WasCanceled || result.WasAborted);
            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.CompletedItems >= 1 && result.CompletedItems < paths.Length,
                result.ToString());
            foreach (var path in paths)
            {
                var moved = Path.Combine(destination.FullName, Path.GetFileName(path));
                Assert.AreNotEqual(File.Exists(path), File.Exists(moved),
                    "Each source must be present in exactly one location.");
            }
            Assert.AreEqual(result.CompletedItems,
                Directory.EnumerateFiles(destination.FullName).Count());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task CopyDirectoryWithNestedFilesCountsTopLevelItems()
    {
        var root = CreateRoot();
        try
        {
            var source = Directory.CreateDirectory(Path.Combine(root, "Source"));
            var destination = Directory.CreateDirectory(Path.Combine(root, "Target"));
            var nested = Directory.CreateDirectory(Path.Combine(source.FullName, "Folder", "Nested"));
            for (var i = 0; i < 4; i++)
                File.WriteAllText(Path.Combine(nested.FullName, $"data-{i}.txt"), $"data-{i}");
            var file = Path.Combine(source.FullName, "standalone.txt");
            File.WriteAllText(file, "standalone");

            var result = await ShellFileOperation.RunDetailedAsync(
                0, FileCommand.Copy, destination.FullName,
                [Path.Combine(source.FullName, "Folder"), file]);

            Assert.IsTrue(result.Succeeded, result.ToString());
            Assert.AreEqual(2, result.CompletedItems);
            Assert.AreEqual(4, Directory.EnumerateFiles(
                Path.Combine(destination.FullName, "Folder"), "*.txt",
                SearchOption.AllDirectories).Count());
            Assert.AreEqual("standalone",
                File.ReadAllText(Path.Combine(destination.FullName, "standalone.txt")));
            Assert.IsTrue(File.Exists(file), "Copy must not remove the source file.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string CreateRoot()
    {
        var path = Path.Combine(Path.GetTempPath(),
            "PaneDock-shell-detailed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
