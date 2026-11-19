using System.IO;
using System.Windows;
using PaneDock.Core.Models;
using PaneDock.Core.Services;

namespace PaneDock.App.Controls;

public partial class ExplorerPane
{
    private void ConfigureWatcher(ExplorerTab tab, string path)
    {
        if (tab.Watcher is not null && SamePath(tab.Watcher.Path, path))
            return;

        tab.Watcher?.Dispose();
        tab.Watcher = null;
        tab.WatchError = null;
        tab.RefreshQueued = false;

        // "내 PC" is a virtual list of drives, not a physical directory.
        if (ExplorerLocations.IsThisPc(path))
            return;

        var watcher = new DirectoryChangeMonitor(path);
        tab.Watcher = watcher;
        tab.WatchError = watcher.LastError?.Message;
        watcher.Changed += (_, _) => PostToUi(() =>
        {
            if (ReferenceEquals(tab.Watcher, watcher))
            {
                if (ReferenceEquals(SelectedTab, tab))
                    Status.Text = "외부 파일 변경 감지 · 목록 갱신 중…";
                RequestSnapshotRefresh(tab);
            }
        });
        watcher.MonitoringFailed += error => PostToUi(() =>
        {
            if (!ReferenceEquals(tab.Watcher, watcher))
                return;
            tab.WatchError = error.Message;
            if (ReferenceEquals(SelectedTab, tab))
                UpdateStatus();
        });
        watcher.MonitoringRestored += (_, _) => PostToUi(() =>
        {
            if (!ReferenceEquals(tab.Watcher, watcher))
                return;
            tab.WatchError = null;
            if (ReferenceEquals(SelectedTab, tab))
                UpdateStatus();
        });
    }

    private void PostToUi(Action action)
    {
        if (_disposed || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return;
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (!_disposed)
                action();
        });
    }

    private void RequestSnapshotRefresh(ExplorerTab tab)
    {
        if (_disposed || tab.IsDisposed || ExplorerLocations.IsThisPc(tab.Path))
            return;

        tab.RefreshQueued = true;
        if (!tab.IsLoading && !tab.IsCheckingPath && !tab.RefreshRunning)
            _ = RefreshSnapshotAsync(tab);
    }

    private async Task RefreshSnapshotAsync(ExplorerTab tab)
    {
        if (tab.RefreshRunning)
            return;

        tab.RefreshRunning = true;
        try
        {
            while (tab.RefreshQueued && !_disposed && !tab.IsDisposed)
            {
                if (tab.IsLoading || tab.IsCheckingPath)
                    break; // A full navigation will resume the pending refresh.

                tab.RefreshQueued = false;
                var path = tab.Path;
                var generation = tab.Generation;

                try
                {
                    var snapshot = await _listing.GetSnapshotAsync(path, tab.Cancellation.Token);
                    if (_disposed || tab.IsDisposed || generation != tab.Generation
                        || !SamePath(tab.Path, path))
                        break;

                    var foldersChanged = ApplySnapshotDiff(tab, snapshot);
                    tab.Error = null;
                    if (foldersChanged)
                        InvalidateTree([path]);
                    if (ReferenceEquals(SelectedTab, tab))
                        UpdateStatus();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                           or InvalidOperationException or ArgumentException
                                           or NotSupportedException)
                {
                    if (generation == tab.Generation && !_disposed && !tab.IsDisposed)
                    {
                        tab.Error = $"자동 갱신 실패: {ex.Message} · F5로 다시 시도";
                        if (ReferenceEquals(SelectedTab, tab))
                            UpdateStatus();
                    }
                    break;
                }
            }
        }
        finally
        {
            tab.RefreshRunning = false;
            if (tab.RefreshQueued && !tab.IsLoading && !tab.IsCheckingPath
                && !_disposed && !tab.IsDisposed)
                _ = RefreshSnapshotAsync(tab);
        }
    }

    private bool ApplySnapshotDiff(ExplorerTab tab, IReadOnlyList<ExplorerEntry> latest)
    {
        var active = ReferenceEquals(SelectedTab, tab);
        var selected = new HashSet<string>(
            (active ? Files.SelectedItems.OfType<ExplorerEntry>() : tab.SelectedEntries)
                .Select(item => item.FullPath), StringComparer.OrdinalIgnoreCase);

        var previousFolders = tab.Items.Where(item => item.IsDirectory)
            .Select(item => item.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentFolders = latest.Where(item => item.IsDirectory)
            .Select(item => item.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var foldersChanged = !previousFolders.SetEquals(currentFolders);

        var currentByPath = latest.ToDictionary(item => item.FullPath, StringComparer.OrdinalIgnoreCase);
        var existingPaths = tab.Items.Select(item => item.FullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _applyingSnapshot = true;
        try
        {
            // WPF DataGrid reads the active CollectionView while it processes
            // ObservableCollection changes. DeferRefresh would make those
            // reads invalid, so apply only the actual differences directly.
            for (var index = tab.Items.Count - 1; index >= 0; index--)
            {
                var old = tab.Items[index];
                if (!currentByPath.TryGetValue(old.FullPath, out var updated))
                    tab.Items.RemoveAt(index);
                else if (old != updated)
                    tab.Items[index] = updated;
            }

            foreach (var added in latest)
            {
                if (!existingPaths.Contains(added.FullPath))
                    tab.Items.Add(added);
            }

            tab.SelectedEntries.Clear();
            tab.SelectedEntries.AddRange(tab.Items.Where(item => selected.Contains(item.FullPath)));
            if (active)
            {
                Files.UnselectAll();
                foreach (var item in tab.SelectedEntries)
                {
                    if (tab.View.Contains(item))
                        Files.SelectedItems.Add(item);
                }
            }
        }
        finally
        {
            _applyingSnapshot = false;
        }

        return foldersChanged;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _filterTimer.Stop();
        foreach (var tab in _tabs)
            tab.Dispose();
    }
}
