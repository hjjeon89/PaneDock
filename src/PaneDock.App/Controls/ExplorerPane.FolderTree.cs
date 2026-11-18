using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace PaneDock.App.Controls;

public partial class ExplorerPane
{
    private Task<string[]>? _treeDrives;
    private int _treeGeneration;
    private bool _treeShowsHidden;
    private bool _selectingTreeNode;

    private void TreeToggle_Changed(object sender, RoutedEventArgs e)
    {
        var show = TreeToggle.IsChecked == true;
        TreeColumn.Width = show ? new GridLength(2, GridUnitType.Star) : new GridLength(0);
        FolderTree.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
        {
            _treeDrives = null;
            _ = SyncFolderTreeAsync();
        }
    }

    private async Task SyncFolderTreeAsync()
    {
        var request = ++_treeGeneration;
        if (TreeToggle.IsChecked != true || SelectedTab is not { Path.Length: > 0 } tab
            || Path.GetPathRoot(tab.Path) is not { Length: > 0 } root)
            return;

        var currentPath = tab.Path;
        if (_treeShowsHidden != tab.ShowHidden)
        {
            _treeShowsHidden = tab.ShowHidden;
            FolderTree.Items.Clear();
        }

        if (FolderTree.Items.Count == 0)
        {
            string[] drives;
            try
            {
                _treeDrives ??= Task.Run(() => DriveInfo.GetDrives().Select(drive => drive.Name).ToArray());
                drives = await _treeDrives;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                drives = [];
            }

            if (request != _treeGeneration || TreeToggle.IsChecked != true)
                return;
            foreach (var drive in drives)
                FolderTree.Items.Add(new FolderNode(drive, drive));
        }

        var node = FindFolder(FolderTree.Items, root);
        if (node is null)
        {
            node = new FolderNode(root, root);
            FolderTree.Items.Add(node);
        }

        foreach (var part in currentPath[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            node.IsExpanded = true;
            await LoadTreeChildrenAsync(node);
            if (request != _treeGeneration || TreeToggle.IsChecked != true)
                return;
            var child = FindFolder(node.Items, Path.Combine(node.FolderPath, part));
            if (child is null)
                break;
            node = child;
        }

        _selectingTreeNode = true;
        try
        {
            node.IsSelected = true;
        }
        finally
        {
            _selectingTreeNode = false;
        }
        await Dispatcher.InvokeAsync(() =>
        {
            if (request == _treeGeneration && TreeToggle.IsChecked == true)
                node.BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    private static FolderNode? FindFolder(ItemCollection items, string path) =>
        items.OfType<FolderNode>().FirstOrDefault(item => SamePath(item.FolderPath, path));

    private async void FolderTree_Expanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is FolderNode node)
            await LoadTreeChildrenAsync(node);
    }

    private Task LoadTreeChildrenAsync(FolderNode node) =>
        node.LoadTask ??= PopulateTreeChildrenAsync(node, node.Version, _treeShowsHidden);

    private async Task PopulateTreeChildrenAsync(FolderNode node, int version, bool showHidden)
    {
        DirectoryInfo[] folders;
        try
        {
            folders = await _listing.GetFoldersAsync(node.FolderPath, showHidden);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (version == node.Version)
            {
                node.Items.Clear();
                node.Items.Add(TreeMessage("폴더를 읽을 수 없음", ex.Message));
                node.LoadTask = null;
            }
            return;
        }

        if (version != node.Version)
            return;
        node.Items.Clear();
        foreach (var folder in folders)
            node.Items.Add(new FolderNode(folder.FullName, folder.Name));
    }

    private async void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_selectingTreeNode || e.NewValue is not FolderNode node || SelectedTab is not { } tab
            || SamePath(node.FolderPath, tab.Path))
            return;

        await NavigateAsync(tab, node.FolderPath, addHistory: true);
        if (ReferenceEquals(tab, SelectedTab) && tab.Error is not null)
            await SyncFolderTreeAsync();
    }

    private void InvalidateTree(string[] paths)
    {
        void Invalidate(ItemCollection items)
        {
            foreach (var node in items.OfType<FolderNode>().ToArray())
            {
                if (!paths.Any(path => SamePath(path, node.FolderPath)))
                {
                    Invalidate(node.Items);
                    continue;
                }

                node.Version++;
                node.LoadTask = null;
                node.Items.Clear();
                node.Items.Add(TreeMessage("불러오는 중…"));
                if (node.IsExpanded && TreeToggle.IsChecked == true)
                    _ = LoadTreeChildrenAsync(node);
            }
        }

        Invalidate(FolderTree.Items);
        if (TreeToggle.IsChecked == true)
            _ = SyncFolderTreeAsync();
    }

    private static TreeViewItem TreeMessage(string message, string? detail = null)
    {
        var item = new TreeViewItem { Header = message, IsEnabled = false, ToolTip = detail };
        item.SetResourceReference(ForegroundProperty, "TextSecondary");
        return item;
    }

    private sealed class FolderNode : TreeViewItem
    {
        public string FolderPath { get; }
        public Task? LoadTask { get; set; }
        public int Version { get; set; }

        public FolderNode(string path, string name)
        {
            FolderPath = path;
            Header = name;
            ToolTip = path;
            SetResourceReference(ForegroundProperty, "TextPrimary");
            Items.Add(TreeMessage("불러오는 중…"));
        }
    }
}
