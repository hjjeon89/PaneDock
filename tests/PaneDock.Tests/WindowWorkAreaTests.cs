using Microsoft.VisualStudio.TestTools.UnitTesting;
using PaneDock.App.Controls;
using static PaneDock.App.Controls.WindowWorkArea;

[TestClass]
public sealed class WindowWorkAreaTests
{
    [TestMethod]
    [DataRow(0, 0, 1920, 1200, 0, 0, 1920, 1152, 0, 0, 1920, 1152)]
    [DataRow(0, 0, 1920, 1080, 0, 48, 1920, 1080, 0, 48, 1920, 1032)]
    [DataRow(-2560, -200, 0, 1240, -2500, -200, 0, 1240, 60, 0, 2500, 1440)]
    [DataRow(1920, 0, 4480, 1440, 1920, 0, 4400, 1440, 0, 0, 2480, 1440)]
    [DataRow(0, -1440, 2560, 0, 0, -1440, 2560, 0, 0, 0, 2560, 1440)]
    public void MaximizationUsesMonitorWorkAreaWithoutChangingTrackLimits(
        int ml, int mt, int mr, int mb, int wl, int wt, int wr, int wb,
        int x, int y, int width, int height)
    {
        var bounds = new MinMaxInfo
        {
            MinTrackSize = new(760, 550),
            MaxTrackSize = new(5000, 3000)
        };

        Assert.IsTrue(ApplyWorkArea(ref bounds, new(ml, mt, mr, mb), new(wl, wt, wr, wb)));
        Assert.AreEqual(new NativePoint(x, y), bounds.MaxPosition);
        Assert.AreEqual(new NativePoint(width, height), bounds.MaxSize);
        Assert.AreEqual(new NativePoint(760, 550), bounds.MinTrackSize);
        Assert.AreEqual(new NativePoint(5000, 3000), bounds.MaxTrackSize);
    }

    [TestMethod]
    public void InvalidWorkAreaPreservesDefaultBounds()
    {
        var bounds = new MinMaxInfo { MaxSize = new(1920, 1080), MaxPosition = new(0, 0) };
        Assert.IsFalse(ApplyWorkArea(ref bounds, new(0, 0, 1920, 1080), new(0, 0, 0, 0)));
        Assert.AreEqual(new NativePoint(1920, 1080), bounds.MaxSize);
        Assert.AreEqual(new NativePoint(0, 0), bounds.MaxPosition);
    }
}
