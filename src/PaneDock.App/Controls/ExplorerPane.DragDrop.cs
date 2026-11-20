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
    private Point? _dragStart;
    private ExplorerEntry? _dragStartItem;

    private void Files_DragSourceMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = null;
        _dragStartItem = null;
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject origin)
            return;

        if (FindParent<DataGridRow>(origin) is { Item: ExplorerEntry entry }
            && !entry.IsDrive && !ExplorerLocations.IsThisPc(entry.FullPath))
        {
            _dragStart = e.GetPosition(Files);
            _dragStartItem = entry;
        }
    }

    private void Files_DragSourceMouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragStart = null;
        _dragStartItem = null;
    }

    private void Files_DragSourceMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || _dragStartItem is not { } entry)
            return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _dragStart = null;
            _dragStartItem = null;
            return;
        }

        var position = e.GetPosition(Files);
        if (Math.Abs(position.X - start.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _dragStart = null;
        _dragStartItem = null;
        var selected = Files.SelectedItems.OfType<ExplorerEntry>()
            .Where(item => !item.IsDrive)
            .ToArray();
        if (!selected.Contains(entry))
            selected = [entry];
        if (selected.Length == 0)
            return;

        var paths = selected.Select(item => item.FullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
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
        catch (Exception ex) when (ex is ExternalException or IOException
                                   or ArgumentException or InvalidOperationException)
        {
            Status.Text = "드래그 시작 실패: " + ex.Message;
        }
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

    private void Files_DragOver(object sender, DragEventArgs e) =>
        ShowCopyDropEffect(e, GetDataGridDropFolder(e));

    private void Pane_DragOver(object sender, DragEventArgs e) =>
        ShowCopyDropEffect(e, SelectedTab?.Path);

    private void FolderTree_DragOver(object sender, DragEventArgs e) =>
        ShowCopyDropEffect(e, GetTreeDropFolder(e));

    private void Tab_DragOver(object sender, DragEventArgs e) =>
        ShowCopyDropEffect(e, sender is TabItem tab ? GetTabDropFolder(tab) : null);

    private void ShowCopyDropEffect(DragEventArgs e, string? folder)
    {
        e.Handled = true;
        var paths = GetDropPaths(e);
        var canCopy = !_fileOperationRunning
            && (e.AllowedEffects & DragDropEffects.Copy) == DragDropEffects.Copy
            && FileDropCopyPolicy.GetRejectionReason(paths, folder, verifyExists: false) is null;
        e.Effects = canCopy ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void Files_Drop(object sender, DragEventArgs e) =>
        await CopyDroppedItemsAsync(e, GetDataGridDropFolder(e));

    private async void Pane_Drop(object sender, DragEventArgs e) =>
        await CopyDroppedItemsAsync(e, SelectedTab?.Path);

    private async void FolderTree_Drop(object sender, DragEventArgs e) =>
        await CopyDroppedItemsAsync(e, GetTreeDropFolder(e));

    private async void Tab_Drop(object sender, DragEventArgs e) =>
        await CopyDroppedItemsAsync(e, sender is TabItem tab ? GetTabDropFolder(tab) : null);

    private async Task CopyDroppedItemsAsync(DragEventArgs e, string? target)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        var sources = GetDropPaths(e);
        if (_fileOperationRunning || (e.AllowedEffects & DragDropEffects.Copy) == 0)
            return;

        var failure = FileDropCopyPolicy.GetRejectionReason(sources, target);
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

            var completed = await ShellFileOperation.RunAsync(
                new WindowInteropHelper(owner).Handle, FileCommand.Copy, target, sources);
            if (completed)
                Status.Text = $"{sources.Length:N0}개 항목 복사 완료";
            else
                Status.Text = "복사가 취소되었거나 일부 항목을 완료하지 못했습니다.";
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
