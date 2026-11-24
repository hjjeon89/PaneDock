using Microsoft.VisualStudio.TestTools.UnitTesting;
using PaneDock.Core.Services;

[TestClass]
public sealed class NetworkDriveResilienceTests
{
    [TestMethod]
    public void UnavailableNetworkPathsAreKeptForReconnectButMissingLocalFoldersAreNot()
    {
        const string unc = @"\\server\share\reports\2026";
        Assert.IsTrue(NetworkPathService.IsNetworkPath(unc));
        Assert.IsTrue(NetworkPathService.IsNetworkPath(@"\\?\UNC\server\share\file.txt"));
        Assert.IsTrue(NetworkPathService.ShouldWaitForReconnect(unc, directoryExists: false));
        Assert.IsFalse(NetworkPathService.ShouldWaitForReconnect(unc, directoryExists: true));
        Assert.IsFalse(NetworkPathService.IsNetworkPath(@"C:\PaneDock\Files"));
        Assert.IsFalse(NetworkPathService.IsNetworkPath(@"\\?\C:\PaneDock\Files"));
        Assert.IsFalse(NetworkPathService.ShouldWaitForReconnect(@"C:\PaneDock\missing",
            directoryExists: false));
        Assert.IsFalse(NetworkPathService.IsNetworkPath(null));
    }

    [TestMethod]
    public async Task WatcherRetriesUnavailableFolderAndRefreshesAfterReconnect()
    {
        var root = CreateTestRoot();
        try
        {
            var target = Path.Combine(root, "unavailable");
            using var monitor = new DirectoryChangeMonitor(target,
                quietPeriod: TimeSpan.FromMilliseconds(100),
                retryPeriod: TimeSpan.FromMilliseconds(150),
                healthPeriod: TimeSpan.FromMilliseconds(150));
            Assert.IsNotNull(monitor.LastError, "Watcher must start in retry mode.");
            var restored = NewSignal();
            var changed = NewSignal();
            monitor.MonitoringRestored += (_, _) => restored.TrySetResult(true);
            monitor.Changed += (_, _) => changed.TrySetResult(true);

            Directory.CreateDirectory(target);
            await restored.Task.WaitAsync(TimeSpan.FromSeconds(8));
            await changed.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.IsNull(monitor.LastError);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task WatcherDetectsSilentFolderLossAndRecoversWhenPathReturns()
    {
        var root = CreateTestRoot();
        try
        {
            var target = Path.Combine(root, "watched");
            Directory.CreateDirectory(target);
            // Windows locks directories watched with ReadDirectoryChangesW.
            // Simulate an unreachable SMB share with an availability probe
            // without physically moving a directory held by the watcher.
            var availability = 1;
            using var monitor = new DirectoryChangeMonitor(target,
                quietPeriod: TimeSpan.FromMilliseconds(100),
                retryPeriod: TimeSpan.FromMilliseconds(150),
                healthPeriod: TimeSpan.FromMilliseconds(150),
                availabilityProbe: _ => Volatile.Read(ref availability) != 0);
            Assert.IsNull(monitor.LastError);

            var failed = NewSignal();
            var restored = NewSignal();
            monitor.MonitoringFailed += _ => failed.TrySetResult(true);
            monitor.MonitoringRestored += (_, _) => restored.TrySetResult(true);

            Interlocked.Exchange(ref availability, 0);
            await failed.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.IsNotNull(monitor.LastError);
            Interlocked.Exchange(ref availability, 1);
            await restored.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.IsNull(monitor.LastError);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task FailedDirectorySnapshotCannotLookLikeAnEmptySuccessfulList()
    {
        var root = CreateTestRoot();
        try
        {
            var folder = Path.Combine(root, "disconnected");
            var service = new DirectoryListingService();
            await Assert.ThrowsExceptionAsync<DirectoryNotFoundException>(async () =>
                await service.GetSnapshotAsync(folder, CancellationToken.None));

            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "after-reconnect.txt"), "preserved");
            var snapshot = await service.GetSnapshotAsync(folder, CancellationToken.None);
            Assert.AreEqual(1, snapshot.Count);
            Assert.AreEqual("after-reconnect.txt", snapshot[0].Name);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void DisposingWatcherDoesNotWaitForBlockedNetworkReconnect()
    {
        var root = CreateTestRoot();
        using var retryStarted = new ManualResetEventSlim();
        using var finishRetry = new ManualResetEventSlim();
        var attempts = 0;
        var monitor = new DirectoryChangeMonitor(Path.Combine(root, "share"),
            retryPeriod: TimeSpan.FromMilliseconds(150),
            healthPeriod: TimeSpan.FromMilliseconds(150),
            availabilityProbe: _ =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                    return false;
                retryStarted.Set();
                finishRetry.Wait(TimeSpan.FromSeconds(6));
                return false;
            });
        try
        {
            Assert.IsTrue(retryStarted.Wait(TimeSpan.FromSeconds(4)),
                "The retry must be active before checking disposal.");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            monitor.Dispose();
            watch.Stop();
            Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(1),
                $"Disposal blocked on network access for {watch.Elapsed.TotalSeconds:F1}s");
        }
        finally
        {
            finishRetry.Set();
            monitor.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void SlowNetworkHealthChecksDoNotAccumulateConcurrentProbes()
    {
        var root = CreateTestRoot();
        using var probeStarted = new ManualResetEventSlim();
        using var releaseProbe = new ManualResetEventSlim();
        var probeCount = 0;
        var folder = Directory.CreateDirectory(Path.Combine(root, "share"));
        var monitor = new DirectoryChangeMonitor(folder.FullName,
            healthPeriod: TimeSpan.FromMilliseconds(100),
            availabilityProbe: _ =>
            {
                if (Interlocked.Increment(ref probeCount) == 1)
                    return true; // Constructor's first availability check
                probeStarted.Set();
                releaseProbe.Wait(TimeSpan.FromSeconds(4));
                return true;
            });
        try
        {
            Assert.IsTrue(probeStarted.Wait(TimeSpan.FromSeconds(3)));
            Thread.Sleep(400); // Multiple timer periods elapse while the probe is blocked
            Assert.AreEqual(2, Volatile.Read(ref probeCount),
                "One blocked SMB check must not spawn more network requests.");
        }
        finally
        {
            releaseProbe.Set();
            monitor.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    private static TaskCompletionSource<bool> NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static string CreateTestRoot()
    {
        var folder = Path.Combine(Path.GetTempPath(),
            "PaneDock-network-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }
}
