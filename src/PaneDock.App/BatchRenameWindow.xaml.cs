using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PaneDock.Core.Models;
using PaneDock.Core.Services;

namespace PaneDock.App;

public partial class BatchRenameWindow : Window
{
    private readonly ObservableCollection<string> _files = [];
    private readonly BatchRenameService _service = new();
    private readonly string _currentFolder;
    private BatchRenamePlan? _preview;
    private BatchRenamePlan? _lastCompleted;
    private CancellationTokenSource? _cancellation;
    private bool _closing;

    public event Action<string[]>? FilesChanged;

    public BatchRenameWindow(IEnumerable<string> selectedFiles, string currentFolder)
    {
        InitializeComponent();
        _currentFolder = currentFolder;
        FilesList.ItemsSource = _files;
        AddFiles(selectedFiles);
        Closing += (_, _) => { _closing = true; _cancellation?.Cancel(); };
    }

    private void AddFiles(IEnumerable<string> filenames)
    {
        if (_cancellation is not null) return;
        foreach (var filename in filenames)
        {
            if (_files.Count >= 10000) break;
            if (!File.Exists(filename) || _files.Contains(filename, StringComparer.OrdinalIgnoreCase))
                continue;
            _files.Add(Path.GetFullPath(filename));
        }
        InvalidatePreview();
        StatusText.Text = $"{_files.Count:N0}개 파일 선택됨. 미리보기 후 변경할 수 있습니다.";
    }

    private void ChooseFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_cancellation is not null) return;
        var dialog = new OpenFileDialog
        {
            Title = "일괄 변경 대상 파일 선택",
            Filter = "모든 파일 (*.*)|*.*",
            Multiselect = true,
            CheckFileExists = true
        };
        if (Directory.Exists(_currentFolder)) dialog.InitialDirectory = _currentFolder;
        if (dialog.ShowDialog(this) == true)
            AddFiles(dialog.FileNames);
    }

    private async void LoadFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_cancellation is not null) return;
        if (!Directory.Exists(_currentFolder))
        {
            StatusText.Text = "현재 경로가 실제 폴더가 아닙니다. 파일 선택 버튼을 사용하세요.";
            return;
        }
        try
        {
            var paths = await Task.Run(() =>
                Directory.EnumerateFiles(_currentFolder).Take(10001).ToArray());
            AddFiles(paths);
            if (paths.Length > 10000)
                StatusText.Text = "한 번에 최대 10,000개 파일만 선택할 수 있습니다.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void RemoveFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_cancellation is not null) return;
        foreach (var path in FilesList.SelectedItems.Cast<string>().ToArray())
            _files.Remove(path);
        InvalidatePreview();
        StatusText.Text = $"{_files.Count:N0}개 파일 선택됨";
    }
    private void ClearFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_cancellation is not null) return;
        _files.Clear();
        InvalidatePreview();
        StatusText.Text = "선택한 파일을 모두 지웠습니다.";
    }

    private void Settings_Changed(object sender, RoutedEventArgs e)
    {
        if (ExtensionBox is not null)
            ExtensionBox.IsEnabled = ChangeExtensionBox?.IsChecked == true;
        InvalidatePreview();
    }

    private void InvalidatePreview()
    {
        _preview = null;
        if (ApplyButton is not null) ApplyButton.IsEnabled = false;
        if (PreviewGrid is not null) PreviewGrid.ItemsSource = null;
    }

    private BatchRenameOptions GetOptions() => new(
        FindBox.Text, ReplaceBox.Text, PrefixBox.Text, SuffixBox.Text,
        ChangeExtensionBox.IsChecked == true, ExtensionBox.Text);

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (_files.Count == 0)
        {
            StatusText.Text = "변경할 파일을 먼저 선택하세요.";
            return;
        }
        _preview = _service.BuildPreview(_files, GetOptions());
        PreviewGrid.ItemsSource = _preview.Items;
        ApplyButton.IsEnabled = _preview.CanExecute;
        StatusText.Text = $"대상 {_preview.Items.Count:N0}개 · 변경 예정 {_preview.ChangeCount:N0}개 · "
            + $"충돌/오류 {_preview.ErrorCount:N0}개"
            + (_preview.ErrorCount > 0 ? " · 오류를 수정하고 다시 미리보기하세요." : "");
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_preview is not { CanExecute: true } plan || _cancellation is not null) return;
        var confirm = MessageBox.Show(this,
            $"{plan.ChangeCount:N0}개 파일의 이름 또는 확장자를 변경하시겠습니까?\n"
            + "기존 파일을 덮어쓰지 않으며, 변경 중 실패하면 자동 복원을 시도합니다.",
            "일괄 변경 확인", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        await ExecutePlanAsync(plan, undo: false);
    }

    private async void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_lastCompleted is not { } completed || _cancellation is not null) return;
        var undoPlan = BatchRenameService.CreateUndoPlan(completed);
        if (MessageBox.Show(this, $"{undoPlan.Items.Count}개 파일명을 이전 이름으로 복원하시겠습니까?",
                "되돌리기 확인", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        await ExecutePlanAsync(undoPlan, undo: true);
    }

    private async Task ExecutePlanAsync(BatchRenamePlan plan, bool undo)
    {
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        SetBusy(true);
        try
        {
            var progress = new Progress<BatchRenameProgress>(value =>
            {
                if (!_closing) StatusText.Text = $"일괄 변경 중… {value.Completed:N0}/{value.Total:N0}";
            });
            var result = await _service.ExecuteAsync(plan, progress, cancellation.Token);
            if (_closing) return;
            StatusText.Text = undo && result.Success
                ? $"{result.ChangedCount:N0}개 파일을 이전 이름으로 복원했습니다."
                : result.Message;
            if (result.Success)
            {
                _lastCompleted = undo ? null : plan;
                UndoButton.IsEnabled = _lastCompleted is not null;
                _preview = null;
                ApplyButton.IsEnabled = false;
                _files.Clear();
                foreach (var item in plan.Items.Where(item => item.Changed))
                {
                    _files.Add(item.TargetPath);
                }
                PreviewGrid.ItemsSource = null;
                FilesChanged?.Invoke(plan.Items.Select(item => item.Folder)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
            }
            else if (result.RecoveryPaths.Count > 0)
                MessageBox.Show(this, result.Message + "\n\n" +
                    string.Join("\n", result.RecoveryPaths.Take(15)),
                    "자동 복원 확인 필요", MessageBoxButton.OK, MessageBoxImage.Warning);
            if (!result.Success)
            {
                _preview = null;
                ApplyButton.IsEnabled = false;
            }
        }
        catch (OperationCanceledException)
        {
            if (!_closing) StatusText.Text = "변경을 중지했습니다.";
        }
        finally
        {
            if (ReferenceEquals(_cancellation, cancellation))
                _cancellation = null;
            if (!_closing) SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        CancelButton.IsEnabled = busy;
        PreviewButton.IsEnabled = !busy;
        ApplyButton.IsEnabled = !busy && _preview?.CanExecute == true;
        UndoButton.IsEnabled = !busy && _lastCompleted is not null;
        FilesList.IsEnabled = !busy;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cancellation?.Cancel();
}
