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
            _ = NavigateAsync(tab, tab.Path, addHistory: false);
        InvalidateTree(folders);
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(Path.TrimEndingDirectorySeparator(left), Path.TrimEndingDirectorySeparator(right),
            StringComparison.OrdinalIgnoreCase);

    private bool CanRunFileCommand(FileCommand command)
    {
        if (_fileOperationRunning || SelectedTab is not { Path.Length: > 0, IsCheckingPath: false })
            return false;

        return command switch
        {
            FileCommand.NewFolder or FileCommand.Paste => true,
            FileCommand.Rename => Files.SelectedItems.Count == 1,
            _ => Files.SelectedItems.Count > 0
        };
    }

    private void Files_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        foreach (var item in Files.ContextMenu.Items.OfType<MenuItem>())
            item.IsEnabled = item.Tag is string tag && Enum.TryParse<FileCommand>(tag, out var command)
                && CanRunFileCommand(command);
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

    private bool HandleFileKey(Key key)
    {
        FileCommand? command = (Keyboard.Modifiers, key) switch
        {
            (ModifierKeys.Control | ModifierKeys.Shift, Key.N) => FileCommand.NewFolder,
            (ModifierKeys.None, Key.F2) => FileCommand.Rename,
            (ModifierKeys.Control, Key.X) => FileCommand.Cut,
            (ModifierKeys.Control, Key.C) => FileCommand.Copy,
            (ModifierKeys.Control, Key.V) => FileCommand.Paste,
            (ModifierKeys.None, Key.Delete) => FileCommand.Delete,
            _ => null
        };
        if (command is null)
            return false;
        _ = RunFileCommandAsync(command.Value);
        return true;
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
            else if (command is FileCommand.NewFolder or FileCommand.Rename)
            {
                var originalName = command == FileCommand.Rename ? selected[0].Name : null;
                name = AskForName(owner, originalName);
                if (name is null || name == originalName)
                    return;
            }

            var changed = new List<string> { folder };
            if (command is FileCommand.Cut or FileCommand.Delete or FileCommand.Rename)
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
                MessageBox.Show(owner, "파일 작업이 취소되었거나 일부 항목을 완료하지 못했습니다.",
                    "PaneDock", MessageBoxButton.OK, MessageBoxImage.Information);
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

    private static string? AskForName(Window owner, string? originalName)
    {
        var title = originalName is null ? "새 폴더" : "이름 변경";
        var input = new TextBox { Text = originalName ?? "새 폴더" };
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
