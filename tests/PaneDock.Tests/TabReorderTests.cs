using Microsoft.VisualStudio.TestTools.UnitTesting;
using PaneDock.Core.Services;

[TestClass]
public sealed class TabReorderTests
{
    [TestMethod]
    public void ReorderLeftAndRightPreservesAllTabs()
    {
        var tabs = new List<string> { "A", "B", "C", "D" };
        TabReorderService.Move(tabs, 0, 3);
        CollectionAssert.AreEqual(new[] { "B", "C", "D", "A" }, tabs);
        TabReorderService.Move(tabs, 3, 0);
        CollectionAssert.AreEqual(new[] { "A", "B", "C", "D" }, tabs);
    }

    [TestMethod]
    public void ReorderRejectsInvalidIndicesWithoutChangingOrder()
    {
        var tabs = new List<string> { "A", "B" };
        Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            TabReorderService.Move(tabs, -1, 1));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            TabReorderService.Move(tabs, 0, 2));
        TabReorderService.Move(tabs, 0, 0);
        CollectionAssert.AreEqual(new[] { "A", "B" }, tabs);
    }
}
