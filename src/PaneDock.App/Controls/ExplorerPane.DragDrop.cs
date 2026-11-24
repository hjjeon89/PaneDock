using System.ComponentModel;
using System.IO;
using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using PaneDock.Core.Models;
using PaneDock.Core.Services;

namespace PaneDock.App.Controls;

public partial class ExplorerPane
{
    private const string TabDragFormat = "PaneDock.InternalTabDrag";
    private static (ExplorerPane Source, TabItem Item)? _activeTabDrag;
    private Point? _tabDragStart;
    private TabItem? _tabDragItem;
    private Point? _dragStart;
    private ExplorerEntry? _dragStartItem;
    private ExplorerEntry[]? _dragStartSelection;

    private void Files_DragSourceMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = null;
        _dragStartItem = null;
        _dragStartSelection = null;
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject origin)
            return;

        if (FindParent<DataGridRow>(origin) is { Item: ExplorerEntry entry }
            && !entry.IsDrive && !ExplorerLocations.IsThisPc(entry.FullPath))
        {
            _dragStart = e.GetPosition(Files);
            _dragStartItem = entry;
            // Capture before DataGrid processes MouseDown: clicking one of
            // several selected rows can collapse the selection to that row.
            // Ctrl/Shift clicks should retain their usual selection behavior.
            if (Keyboard.Modifiers == ModifierKeys.None && Files.SelectedItems.Contains(entry))
                _dragStartSelection = Files.SelectedItems.OfType<ExplorerEntry>().ToArray();
        }
    }

    private void Files_DragSourceMouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragStart = null;
        _dragStartItem = null;
        _dragStartSelection = null;
    }

    private void Files_DragSourceMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || _dragStartItem is not { } entry)
            return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _dragStart = null;
            _dragStartItem = null;
            _dragStartSelection = null;
            return;
        }

        var position = e.GetPosition(Files);
        if (Math.Abs(position.X - start.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var selected = _dragStartSelection is { Length: > 1 } snapshot
            ? snapshot : Files.SelectedItems.OfType<ExplorerEntry>().ToArray();
        var paths = FileDropCopyPolicy.GetDraggedPaths(selected, entry);
        _dragStart = null;
        _dragStartItem = null;
        _dragStartSelection = null;
        if (paths.Length == 0)
            return;

        // Restore multi-selection visually if DataGrid collapsed it between
        // preview MouseDown and the drag threshold.
        if (selected.Length > 1)
        {
            foreach (var item in selected)
            {
                if (Files.Items.Contains(item) && !Files.SelectedItems.Contains(item))
                    Files.SelectedItems.Add(item);
            }
        }
        var files = new StringCollection();
        files.AddRange(paths);
        var data = new DataObject();
        data.SetFileDropList(files);
        // Always advertise Copy, never Move. Shift-key dragging must not delete sources.
        using var preferredEffect = new MemoryStream(BitConverter.GetBytes(1));
        data.SetData("Preferred DropEffect", preferredEffect, autoConvert: false);
        try
        {
            e.Handled = true;
            DragDrop.DoDragDrop(Files, data, DragDropEffects.Copy);
        }
        catch (Exception ex) when (ShellFileOperation.IsUserCancellation(ex))
        {
            // Esc or cancellation of a Windows drag is not an error.
        }
        catch (Exception ex) when (ex is ExternalException or IOException
                                    or ArgumentException or InvalidOperationException)
        {
            Status.Text = "드래그 시작 실패: " + ex.Message;
        }
    }

    private void Tab_DragStartMouseDown(object sender, MouseButtonEventArgs e)
    {
        ClearPendingTabDrag();
        if (sender is not TabItem tabItem || e.ChangedButton != MouseButton.Left)
            return;
        // Clicking the tab's X button must close it, never start dragging.
        if (e.OriginalSource is DependencyObject origin
            && FindParent<Button>(origin) is not null)
            return;
        _tabDragStart = e.GetPosition(Tabs);
        _tabDragItem = tabItem;
    }

    internal void ClearPendingTabDrag()
    {
        _tabDragStart = null;
        _tabDragItem = null;
    }

    // MainWindow monitors preview movement across all panes. A fast vertical
    // movement can leave the small tab header before the first MouseMove.
    internal void TryStartPendingTabDrag(MouseEventArgs e)
    {
        if (_tabDragStart is not { } start || _tabDragItem is not { } item)
            return;
        if (_disposed || !IsVisible || !Tabs.Items.Contains(item)
            || e.LeftButton != MouseButtonState.Pressed)
        {
            ClearPendingTabDrag();
            return;
        }
        var point = e.GetPosition(Tabs);
        if (Math.Abs(point.X - start.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(point.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        ClearPendingTabDrag();
        var data = new DataObject();
        data.SetData(TabDragFormat, "PaneDockTab");
        _activeTabDrag = (this, item);
        try
        {
            e.Handled = true;
            DragDrop.DoDragDrop(item, data, DragDropEffects.Move);
        }
        catch (Exception ex) when (ShellFileOperation.IsUserCancellation(ex))
        {
            // Dropping a dragged tab nowhere, or canceling it, is harmless.
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
        {
            Status.Text = "탭 이동을 시작할 수 없습니다: " + ex.Message;
        }
        finally { _activeTabDrag = null; }
    }

    private static bool IsInternalTabDrag(DragEventArgs e) =>
        _activeTabDrag is not null && e.Data.GetDataPresent(TabDragFormat);

    private void ShowTabTransferEffect(DragEventArgs e, TabItem? target)
    {
        e.Handled = true;
        var valid = _activeTabDrag is { } source
            && (e.AllowedEffects & DragDropEffects.Move) != 0
            // Moving within the same pane requires another tab header.
            // Dropping on the source tab or blank pane area does nothing.
            && (!ReferenceEquals(source.Source, this)
                || target is not null && !ReferenceEquals(source.Item, target));
        e.Effects = valid ? DragDropEffects.Move : DragDropEffects.None;
    }

    private void DropInternalTab(DragEventArgs e, TabItem? target)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if (_activeTabDrag is not { } drag || !IsInternalTabDrag(e))
            return;
        if (ReferenceEquals(drag.Source, this))
        {
            if (target is null || ReferenceEquals(target, drag.Item))
                return;
            var from = Tabs.Items.IndexOf(drag.Item);
            var to = Tabs.Items.IndexOf(target);
            if (from < 0 || to < 0) return;
            _changingTab = true;
            try
            {
                var moving = Tabs.Items[from];
                Tabs.Items.RemoveAt(from);
                Tabs.Items.Insert(to, moving);
                TabReorderService.Move(_tabs, from, to);
                Tabs.SelectedItem = moving;
            }
            finally { _changingTab = false; }
            if (SelectedTab is { } selected)
                ShowTab(selected);
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            // Reuse the existing source->destination transfer protocol. It
            // preserves the active tab's path, filter and sort state.
            drag.Source.Tabs.SelectedItem = drag.Item;
            drag.Source.MoveTabRequested?.Invoke(drag.Source, Number);
        }
        e.Effects = DragDropEffects.Move;
    }

    private static string[]? GetDropPaths(DragEventArgs e)
    {
        try
        {
            return e.Data.GetDataPresent(DataFormats.FileDrop)
                ? e.Data.GetData(DataFormats.FileDrop) as string[]
                : null;
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException)
        {
            return null;
        }
    }

    private string? GetDataGridDropFolder(DragEventArgs e)
    {
        if (e.OriginalSource is DependencyObject origin
            && FindParent<DataGridRow>(origin) is { Item: ExplorerEntry { IsDirectory: true } directory })
            return directory.FullPath;
        return SelectedTab?.Path;
    }

    private string? GetTreeDropFolder(DragEventArgs e)
    {
        if (e.OriginalSource is DependencyObject origin
            && FindParent<FolderNode>(origin) is { } node)
            return node.FolderPath;
        return SelectedTab?.Path;
    }

    private static string? GetTabDropFolder(TabItem tabItem) =>
        tabItem.Tag is ExplorerTab tab
            ? string.IsNullOrEmpty(tab.Path) ? tab.RequestedPath : tab.Path
            : null;

    private void Files_DragOver(object sender, DragEventArgs e)
    {
        if (IsInternalTabDrag(e))
            ShowTabTransferEffect(e, null);
        else
            ShowCopyDropEffect(e, GetDataGridDropFolder(e));
    }

    private void Pane_DragOver(object sender, DragEventArgs e)
    {
        if (IsInternalTabDrag(e))
            ShowTabTransferEffect(e, null);
        else
            ShowCopyDropEffect(e, SelectedTab?.Path);
    }

    private void FolderTree_DragOver(object sender, DragEventArgs e)
    {
        if (IsInternalTabDrag(e))
            ShowTabTransferEffect(e, null);
        else
            ShowCopyDropEffect(e, GetTreeDropFolder(e));
    }

    private void Tab_DragOver(object sender, DragEventArgs e)
    {
        if (IsInternalTabDrag(e))
            ShowTabTransferEffect(e, sender as TabItem);
        else
            ShowCopyDropEffect(e, sender is TabItem tab ? GetTabDropFolder(tab) : null);
    }

    private void ShowCopyDropEffect(DragEventArgs e, string? folder)
    {
        e.Handled = true;
        var paths = GetDropPaths(e);
        var canCopy = !_fileOperationRunning
            && (e.AllowedEffects & DragDropEffects.Copy) == DragDropEffects.Copy
            && FileDropCopyPolicy.GetRejectionReason(paths, folder, verifyExists: false) is null;
        e.Effects = canCopy ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void Files_Drop(object sender, DragEventArgs e)
    {
        if (IsInternalTabDrag(e))
            DropInternalTab(e, null);
        else
            await CopyDroppedItemsAsync(e, GetDataGridDropFolder(e));
    }

    private async void Pane_Drop(object sender, DragEventArgs e)
    {
        if (IsInternalTabDrag(e))
            DropInternalTab(e, null);
        else
            await CopyDroppedItemsAsync(e, SelectedTab?.Path);
    }

    private async void FolderTree_Drop(object sender, DragEventArgs e)
    {
        if (IsInternalTabDrag(e))
            DropInternalTab(e, null);
        else
            await CopyDroppedItemsAsync(e, GetTreeDropFolder(e));
    }

    private async void Tab_Drop(object sender, DragEventArgs e)
    {
        if (IsInternalTabDrag(e))
            DropInternalTab(e, sender as TabItem);
        else
            await CopyDroppedItemsAsync(e, sender is TabItem tab ? GetTabDropFolder(tab) : null);
    }

    private async Task CopyDroppedItemsAsync(DragEventArgs e, string? target)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        var sources = GetDropPaths(e);
        if (_fileOperationRunning || (e.AllowedEffects & DragDropEffects.Copy) == 0)
            return;

        // During Drop, avoid synchronous Directory.Exists/File.Exists against
        // a possibly disconnected SMB share. The complete preflight below
        // runs on a worker thread before any Shell operation is queued.
        var failure = FileDropCopyPolicy.GetRejectionReason(sources, target,
            verifyExists: false);
        if (failure is not null)
        {
            Status.Text = failure;
            return;
        }
        if (target is null || sources is null || Window.GetWindow(this) is not { } owner)
            return;

        // A successful drag is only a copy; no move, cut, or clipboard state is changed.
        e.Effects = DragDropEffects.Copy;
        _fileOperationRunning = true;
        try
        {
            var check = await Task.Run(() => FileTransferPreflight.Check(sources, target));
            if (!check.CanProceed)
            {
                Status.Text = check.Error!;
                return;
            }
            if (!ConfirmFileConflicts(owner, check.Collisions))
            {
                Status.Text = "이름 충돌이 있는 복사를 취소했습니다.";
                return;
            }

            var result = await ShellFileOperation.RunDetailedAsync(
                new WindowInteropHelper(owner).Handle, FileCommand.Copy, target, sources);
            ShowFileFeedback(result.Describe(FileCommand.Copy));
            if (result.Error is not null)
                MessageBox.Show(owner, result.Describe(FileCommand.Copy)
                    + "\n\n" + result.Error, "파일 복사 오류",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex) when (ex is ExternalException or IOException or ArgumentException
                                   or UnauthorizedAccessException or NotSupportedException)
        {
            Status.Text = "복사 실패: " + ex.Message;
            MessageBox.Show(owner, "항목을 복사할 수 없습니다.\n\n" + ex.Message,
                "PaneDock", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _fileOperationRunning = false;
            FileLocationsChanged?.Invoke([target]);
        }
    }
}
