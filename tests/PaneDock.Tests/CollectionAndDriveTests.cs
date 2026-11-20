using System.Collections.Specialized;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PaneDock.Core.Services;

[TestClass]
public sealed class CollectionAndDriveTests
{
    [TestMethod]
    public void LargeDirectoryBatchRaisesOneResetInsteadOfThousandsOfEvents()
    {
        var items = new BatchObservableCollection<int>();
        var changes = new List<NotifyCollectionChangedAction>();
        items.CollectionChanged += (_, e) => changes.Add(e.Action);
        items.AddRange(Enumerable.Range(0, 10000));
        Assert.AreEqual(10000, items.Count);
        CollectionAssert.AreEqual(
            new[] { NotifyCollectionChangedAction.Reset }, changes.ToArray());
        items.AddRange([]);
        Assert.AreEqual(1, changes.Count, "Empty batch should not invalidate the UI.");
    }

    [TestMethod]
    public void DriveDetectorIgnoresOrderingAndDetectsReconnects()
    {
        var detector = new DriveChangeDetector();
        Assert.IsFalse(detector.Update([@"C:\", @"D:\"]));
        Assert.IsFalse(detector.Update([@"d:\", @"C:\"]));
        Assert.IsTrue(detector.Update([@"C:\"]));
        Assert.IsTrue(detector.Update([@"C:\", @"D:\"]));
    }
}
