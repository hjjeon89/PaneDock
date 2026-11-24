using Microsoft.VisualStudio.TestTools.UnitTesting;
using PaneDock.Core.Services;
using PaneDock.App;
using System.Runtime.InteropServices;

[TestClass]
public sealed class FileTransferSafetyTests
{
    [TestMethod]
    public void ShellCanceledByUserIsNotReportedAsAnError()
    {
        Assert.IsTrue(ShellFileOperation.IsUserCancellation(
            new COMException("User canceled", unchecked((int)0x800704C7))));
        Assert.IsTrue(ShellFileOperation.IsUserCancellation(
            new COMException("Operation aborted", unchecked((int)0x80004004))));
        Assert.IsTrue(ShellFileOperation.IsUserCancellation(new OperationCanceledException()));
        Assert.IsTrue(ShellFileOperation.IsUserCancellation(
            new COMException("Shell canceled", unchecked((int)0x80270000))));
        Assert.IsTrue(ShellFileOperation.IsUserCancellation(
            new COMException("Shell stopped", unchecked((int)0x80270001))));
        Assert.IsFalse(ShellFileOperation.IsUserCancellation(
            new COMException("Access denied", unchecked((int)0x80070005))));
    }

    [TestMethod]
    public void ShellFailureMessageKeepsActionableHResult()
    {
        var accessDenied = ShellFileOperation.DescribeShellFailure(unchecked((int)0x80070005));
        StringAssert.Contains(accessDenied, "0x80070005");
        Assert.IsTrue(accessDenied.Length > "0x80070005".Length,
            "A failing Shell item should include an explanation and the original error code.");
        var partialFailure = new ShellFileOperationResult(3, 2, 1, false, false, accessDenied);
        Assert.IsFalse(partialFailure.Succeeded);
        StringAssert.Contains(partialFailure.Describe(FileCommand.Copy), "1개 미완료");
        StringAssert.Contains(partialFailure.Describe(FileCommand.Copy), "오류 발생");
    }

    [TestMethod]
    public void FileOperationSummaryDistinguishesSuccessPartialAndSkip()
    {
        var success = new ShellFileOperationResult(3, 3, 0, false, false);
        Assert.IsTrue(success.Succeeded);
        StringAssert.Contains(success.Describe(FileCommand.Copy), "3개 항목 복사 완료");

        var partial = new ShellFileOperationResult(3, 1, 0, true, true);
        Assert.IsFalse(partial.Succeeded);
        StringAssert.Contains(partial.Describe(FileCommand.Cut), "1개 이동 완료");
        StringAssert.Contains(partial.Describe(FileCommand.Cut), "2개 미완료");

        var skipped = new ShellFileOperationResult(3, 2, 0, false, false, SkippedItems: 1);
        Assert.IsFalse(skipped.Succeeded);
        StringAssert.Contains(skipped.Describe(FileCommand.Copy), "건너뛴 항목");
    }

    [TestMethod]
    public void CopyPreflightFindsExistingTargetsAndNeverModifiesFiles()
    {
        var root = CreateRoot();
        try
        {
            var source = Directory.CreateDirectory(Path.Combine(root, "From"));
            var destination = Directory.CreateDirectory(Path.Combine(root, "To"));
            var original = Path.Combine(source.FullName, "same.log");
            var existing = Path.Combine(destination.FullName, "same.log");
            File.WriteAllText(original, "source");
            File.WriteAllText(existing, "destination");

            var check = FileTransferPreflight.Check([original], destination.FullName);
            Assert.IsTrue(check.CanProceed);
            Assert.AreEqual(1, check.Collisions.Count);
            Assert.AreEqual(existing, check.Collisions[0]);
            Assert.AreEqual("source", File.ReadAllText(original));
            Assert.AreEqual("destination", File.ReadAllText(existing));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void MovePreflightRejectsSameFolderAndNestedFolder()
    {
        var root = CreateRoot();
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(root, "Parent"));
            var nested = Directory.CreateDirectory(Path.Combine(folder.FullName, "Child"));
            var item = Path.Combine(folder.FullName, "file.txt");
            File.WriteAllText(item, "keep");
            Assert.IsFalse(FileTransferPreflight.Check([item], folder.FullName, moving: true).CanProceed);
            Assert.IsFalse(FileTransferPreflight.Check(
                [folder.FullName], nested.FullName, moving: false).CanProceed);
            Assert.IsFalse(FileTransferPreflight.Check(
                [folder.FullName], nested.FullName, moving: true).CanProceed);
            Assert.AreEqual("keep", File.ReadAllText(item));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void InvalidSourcesAndDisconnectedDestinationAreRejected()
    {
        var root = CreateRoot();
        try
        {
            var destination = Directory.CreateDirectory(Path.Combine(root, "Destination"));
            var original = Path.Combine(root, "original.txt");
            File.WriteAllText(original, "keep");
            Assert.IsFalse(FileTransferPreflight.Check([], destination.FullName).CanProceed);
            Assert.IsFalse(FileTransferPreflight.Check([original, original], destination.FullName).CanProceed);
            Assert.IsFalse(FileTransferPreflight.Check(["relative.txt"], destination.FullName).CanProceed);
            Assert.IsFalse(FileTransferPreflight.Check(
                [Path.Combine(root, "missing.txt")], destination.FullName).CanProceed);
            Assert.IsFalse(FileTransferPreflight.Check(
                [original], Path.Combine(root, "disconnected")).CanProceed);
            Assert.AreEqual("keep", File.ReadAllText(original));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void SameTargetNamesOrNestedSourcesAreRejectedWithoutChanges()
    {
        var root = CreateRoot();
        try
        {
            var first = Directory.CreateDirectory(Path.Combine(root, "first"));
            var second = Directory.CreateDirectory(Path.Combine(root, "second"));
            var target = Directory.CreateDirectory(Path.Combine(root, "target"));
            var a = Path.Combine(first.FullName, "same.txt");
            var b = Path.Combine(second.FullName, "SAME.TXT");
            File.WriteAllText(a, "original A");
            File.WriteAllText(b, "original B");
            var check = FileTransferPreflight.Check([a, b], target.FullName);
            Assert.IsFalse(check.CanProceed);
            StringAssert.Contains(check.Error!, "동일한 이름");

            var nested = Path.Combine(first.FullName, "child.txt");
            File.WriteAllText(nested, "keep");
            var overlap = FileTransferPreflight.Check([first.FullName, nested], target.FullName,
                moving: true);
            Assert.IsFalse(overlap.CanProceed);
            StringAssert.Contains(overlap.Error!, "하위 항목");

            Assert.IsFalse(FileTransferPreflight.Check([a, ""], target.FullName).CanProceed);
            Assert.AreEqual("original A", File.ReadAllText(a));
            Assert.AreEqual("original B", File.ReadAllText(b));
            Assert.AreEqual("keep", File.ReadAllText(nested));
            Assert.AreEqual(0, Directory.GetFileSystemEntries(target.FullName).Length);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string CreateRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "PaneDock-transfer-safe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
