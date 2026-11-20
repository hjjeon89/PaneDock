using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PaneDock.App.Controls;
using PaneDock.Core.Models;
using PaneDock.Core.Services;

[TestClass]
public sealed class NewFileAndShellIconTests
{
    [TestMethod]
    public async Task NewFileIsEmptyAndExistingFileIsNeverOverwritten()
    {
        var directory = Path.Combine(Path.GetTempPath(),
            "PaneDock-new-file-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            var creator = new NewFileService();
            var path = await creator.CreateEmptyAsync(directory, "new-report.txt");
            Assert.IsTrue(File.Exists(path));
            Assert.AreEqual(0L, new FileInfo(path).Length);
            File.WriteAllText(path, "preserve-existing-data");
            await Assert.ThrowsExceptionAsync<IOException>(async () =>
                await creator.CreateEmptyAsync(directory, "new-report.txt"));
            Assert.AreEqual("preserve-existing-data", File.ReadAllText(path));
            await Assert.ThrowsExceptionAsync<ArgumentException>(async () =>
                await creator.CreateEmptyAsync(directory, @"..\outside.txt"));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void WindowsAssociatedIconsAreReusedForMatchingFileTypes()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Exception? failure = null;
        var worker = new Thread(() =>
        {
            try
            {
                var converter = new ShellIconConverter();
                var first = new ExplorerEntry(@"C:\temp\first.txt", "first.txt", ".txt",
                    0, DateTime.MinValue, false);
                var second = first with { FullPath = @"C:\temp\second.txt", Name = "second.txt" };
                var firstIcon = converter.Convert(first, typeof(object), null!,
                    CultureInfo.InvariantCulture);
                var secondIcon = converter.Convert(second, typeof(object), null!,
                    CultureInfo.InvariantCulture);
                Assert.IsNotNull(firstIcon, "A common Windows text-file association should have a Shell icon.");
                Assert.AreSame(firstIcon, secondIcon, "The Shell icon cache should be reused by file extension.");
                Assert.IsNotNull(converter.Convert(first with { IsDirectory = true },
                    typeof(object), null!, CultureInfo.InvariantCulture));
            }
            catch (Exception ex) { failure = ex; }
        });
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        Assert.IsTrue(worker.Join(TimeSpan.FromSeconds(15)), "Windows Shell icon lookup timed out.");
        if (failure is not null)
            throw new AssertFailedException("Windows Shell icon test failed: " + failure);
    }
}
