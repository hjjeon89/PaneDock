using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using PaneDock.Core.Models;
using PaneDock.Core.Services;

namespace PaneDock.App.Controls;

public partial class ExplorerPane
{
    private const string PreferredDropEffect = "Preferred DropEffect";
    private static bool _fileOperationRunning;

    internal event Action<string[]>? FileLocationsChanged;

    internal void RefreshLocations(string[] folders)
    {
        foreach (var tab in _tabs.Where(tab => !tab.IsCheckingPath && folders.Any(folder => SamePath(tab.Path, folder))))
            RequestSnapshotRefresh(tab);
        InvalidateTree(folders);
    }

    internal void RefreshDriveList()
    {
        foreach (var tab in _tabs.Where(tab =>
                     ExplorerLocations.IsThisPc(tab.Path)))
            RequestSnapshotRefresh(tab);
    }

    private static bool SamePath(string left, string right) =>
        ExplorerLocations.IsThisPc(left) || ExplorerLocations.IsThisPc(right)
            ? ExplorerLocations.IsThisPc(left) && ExplorerLocations.IsThisPc(right)
            : string.Equals(Path.TrimEndingDirectorySeparator(left), Path.TrimEndingDirectorySeparator(right),
                StringComparison.OrdinalIgnoreCase);

    private bool CanRunFileCommand(FileCommand command)
    {
        if (_fileOperationRunning || SelectedTab is not { Path.Length: > 0, IsCheckingPath: false }
            || ExplorerLocations.IsThisPc(SelectedTab.Path))
            return false;

        return command switch
        {
            FileCommand.NewFolder or FileCommand.NewFile or FileCommand.Paste => true,
            FileCommand.Rename => Files.SelectedItems.Count == 1,
            _ => Files.SelectedItems.Count > 0
        };
    }

    private void Files_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        => UpdateFileContextMenu();

    private void UpdateFileContextMenu()
    {
        foreach (var item in Files.ContextMenu.Items.OfType<MenuItem>())
            item.IsEnabled = item.Tag is string tag && Enum.TryParse<FileCommand>(tag, out var command)
                && CanRunFileCommand(command);
    }

    private void ShowFileContextMenu()
    {
        if (Files.ContextMenu is not { } menu)
            return;
        UpdateFileContextMenu();
        menu.PlacementTarget = Files;
        menu.IsOpen = true;
    }

    private void Files_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindParent<DataGridRow>(source) is { } row)
        {
            if (!row.IsSelected)
                Files.SelectedItem = row.Item;
        }
        else
        {
            Files.UnselectAll();
        }
    }

    private async void FileCommand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && Enum.TryParse<FileCommand>(tag, out var command))
            await RunFileCommandAsync(command);
    }

    private async Task RunFileCommandAsync(FileCommand command)
    {
        if (!CanRunFileCommand(command) || SelectedTab is not { } tab || Window.GetWindow(this) is not { } owner)
            return;

        _fileOperationRunning = true;
        try
        {
            var selected = Files.SelectedItems.OfType<ExplorerEntry>().ToArray();
            var folder = tab.Path;
            if (command is FileCommand.Copy or FileCommand.Cut)
            {
                var files = new StringCollection();
                files.AddRange(selected.Select(entry => entry.FullPath).ToArray());
                var data = new DataObject();
                data.SetFileDropList(files);
                using var effect = new MemoryStream();
                effect.Write(BitConverter.GetBytes(command == FileCommand.Cut ? 2 : 1));
                effect.Position = 0;
                data.SetData(PreferredDropEffect, effect, autoConvert: false);
                Clipboard.SetDataObject(data, copy: true);
                Status.Text = command == FileCommand.Cut
                    ? $"{selected.Length:N0}개 잘라내기" : $"{selected.Length:N0}개 복사";
                return;
            }

            string[] paths = command is FileCommand.Rename or FileCommand.Delete
                or FileCommand.DeletePermanently
                ? selected.Select(entry => entry.FullPath).ToArray() : [];
            uint clipboardSequence = 0;
            string? name = null;
            if (command == FileCommand.Paste)
            {
                var beforeRead = GetClipboardSequenceNumber();
                var data = Clipboard.GetDataObject();
                if (data is null || !data.TryGetData<string[]>(DataFormats.FileDrop, out var clipboardPaths)
                    || clipboardPaths is not { Length: > 0 })
                {
                    Status.Text = "붙여넣을 파일이나 폴더가 없습니다.";
                    return;
                }

                paths = clipboardPaths;
                var dropEffect = 1;
                if (data.GetDataPresent(PreferredDropEffect, autoConvert: false))
                {
                    dropEffect = data.TryGetData<MemoryStream>(PreferredDropEffect, autoConvert: false, out var effect)
                        && effect!.Length >= 4 ? BitConverter.ToInt32(effect.ToArray()) : 0;
                }
                if (dropEffect is not (1 or 2))
                {
                    Status.Text = "붙여넣을 수 있는 파일 복사 또는 이동 데이터가 아닙니다.";
                    return;
                }
                command = dropEffect == 2 ? FileCommand.Cut : FileCommand.Copy;
                if (beforeRead == GetClipboardSequenceNumber())
                    clipboardSequence = beforeRead;
            }
            else if (command is FileCommand.NewFolder or FileCommand.NewFile or FileCommand.Rename)
            {
                var originalName = command == FileCommand.Rename ? selected[0].Name : null;
                name = AskForName(owner, originalName, command == FileCommand.NewFile);
                if (name is null || name == originalName)
                    return;
                var newPath = Path.Combine(folder, name);
                if (!string.Equals(newPath,
                        command == FileCommand.Rename ? selected[0].FullPath : null,
                        StringComparison.OrdinalIgnoreCase)
                    && (File.Exists(newPath) || Directory.Exists(newPath)))
                {
                    MessageBox.Show(owner, "해당 이름의 파일이나 폴더가 이미 있습니다.\n\n"
                        + newPath, "이름 충돌", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            if (command == FileCommand.NewFile)
            {
                var created = await new NewFileService().CreateEmptyAsync(folder, name!);
                Status.Text = "새 파일 생성: " + Path.GetFileName(created);
                FileLocationsChanged?.Invoke([folder]);
                return;
            }

            if (command is FileCommand.Copy or FileCommand.Cut)
            {
                var check = await Task.Run(() =>
                    FileTransferPreflight.Check(paths, folder, moving: command == FileCommand.Cut));
                if (!check.CanProceed)
                {
                    Status.Text = check.Error!;
                    MessageBox.Show(owner, check.Error!, "파일 작업 확인",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (!ConfirmFileConflicts(owner, check.Collisions))
                    return;
            }
            else if (command is FileCommand.Delete or FileCommand.DeletePermanently)
            {
                var folderCount = selected.Count(entry => entry.IsDirectory);
                var prompt = $"{selected.Length:N0}개 항목을 삭제하시겠습니까?\n"
                    + $"파일 {selected.Length - folderCount:N0}개 / 폴더 {folderCount:N0}개\n\n"
                    + (command == FileCommand.DeletePermanently
                        ? "휴지통을 거치지 않고 영구 삭제합니다. 복구할 수 없습니다."
                        : "Windows 휴지통 이동을 요청합니다. 폴더 내부의 항목도 함께 삭제되며, "
                          + "휴지통이 없는 위치에서는 Windows가 영구 삭제를 경고할 수 있습니다.");
                if (MessageBox.Show(owner, prompt,
                        command == FileCommand.DeletePermanently ? "영구 삭제 확인" : "삭제 확인",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning,
                        MessageBoxResult.No) != MessageBoxResult.Yes)
                    return;
            }

            var changed = new List<string> { folder };
            if (command is FileCommand.Cut or FileCommand.Delete or
                FileCommand.DeletePermanently or FileCommand.Rename)
            {
                foreach (var path in paths)
                {
                    if (Path.IsPathFullyQualified(path) && Path.GetDirectoryName(path) is { } parent)
                        changed.Add(parent);
                }
            }

            bool completed;
            try
            {
                completed = await ShellFileOperation.RunAsync(new WindowInteropHelper(owner).Handle, command, folder, paths, name);
            }
            finally
            {
                FileLocationsChanged?.Invoke(changed.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
            }

            if (!completed)
                Status.Text = "파일 작업이 취소되었거나 일부 항목이 처리되지 않았습니다.";
            else if (command == FileCommand.Cut)
                ClearClipboardIfUnchanged(clipboardSequence, new WindowInteropHelper(owner).Handle);
        }
        catch (Exception ex) when (ex is ExternalException or IOException or ArgumentException
                                   or UnauthorizedAccessException or NotSupportedException)
        {
            MessageBox.Show(owner, $"파일 작업을 완료할 수 없습니다.\n\n{ex.Message}",
                "PaneDock", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _fileOperationRunning = false;
        }
    }

    private static bool ConfirmFileConflicts(Window owner, IReadOnlyList<string> collisions)
    {
        if (collisions.Count == 0) return true;
        var sample = string.Join("\n", collisions.Take(4));
        var message = $"대상 폴더에 같은 이름의 항목이 {collisions.Count:N0}개 있습니다.\n\n"
            + sample + (collisions.Count > 4 ? "\n…" : "") + "\n\n"
            + "계속하면 Windows에서 충돌 처리 방법을 묻게 됩니다. "
            + "기존 파일을 보존하려면 '바꾸기'를 선택하지 마세요.\n\n계속하시겠습니까?";
        return MessageBox.Show(owner, message, "이름 충돌 확인",
            MessageBoxButton.YesNo, MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    private static string? AskForName(Window owner, string? originalName, bool newFile = false)
    {
        var title = newFile ? "새 파일 만들기" : originalName is null ? "새 폴더" : "이름 변경";
        var input = new TextBox { Text = originalName ?? (newFile ? "새 텍스트 문서.txt" : "새 폴더") };
        AutomationProperties.SetName(input, "이름");
        var error = new TextBlock { Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
        var confirm = new Button { Content = "확인", IsDefault = true, MinWidth = 70 };
        var cancel = new Button { Content = "취소", IsCancel = true, MinWidth = 70 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(confirm);
        buttons.Children.Add(cancel);
        var content = new StackPanel { Margin = new Thickness(14) };
        content.Children.Add(input);
        content.Children.Add(error);
        content.Children.Add(buttons);
        var dialog = new Window
        {
            Owner = owner, Title = title, Width = 360, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = content
        };
        dialog.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        confirm.Click += (_, _) =>
        {
            if (WindowsItemName.IsValid(input.Text))
                dialog.DialogResult = true;
            else
                error.Text = "Windows에서 사용할 수 있는 파일 또는 폴더 이름을 입력하세요.";
        };
        return dialog.ShowDialog() == true ? input.Text : null;
    }

    private static void ClearClipboardIfUnchanged(uint sequence, nint owner)
    {
        if (sequence == 0 || !OpenClipboard(owner))
            return;
        try
        {
            if (sequence == GetClipboardSequenceNumber())
                EmptyClipboard();
        }
        finally
        {
            CloseClipboard();
        }
    }

    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] private static extern bool OpenClipboard(nint owner);
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
}
