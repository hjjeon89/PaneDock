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
        Assert.IsFalse(ShellFileOperation.IsUserCancellation(
            new COMException("Access denied", unchecked((int)0x80070005))));
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

    private static string CreateRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "PaneDock-transfer-safe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
