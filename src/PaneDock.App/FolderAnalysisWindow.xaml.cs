using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PaneDock.Core.Models;
using PaneDock.Core.Services;

namespace PaneDock.App;

public partial class FolderAnalysisWindow : Window
{
    private readonly ObservableCollection<string> _folders = [];
    private readonly ObservableCollection<FolderAnalysisItem> _results = [];
    private readonly FolderAnalysisService _service = new();
    private CancellationTokenSource? _cancellation;
    private bool _closing;

    public FolderAnalysisWindow(IEnumerable<string> initialPaths)
    {
        InitializeComponent();
        FoldersBox.ItemsSource = _folders;
        ResultsGrid.ItemsSource = _results;
        foreach (var path in initialPaths)
            AddFolder(path);
        Closing += (_, _) => { _closing = true; _cancellation?.Cancel(); };
    }

    private void AddFolder(string path)
    {
        if (_cancellation is not null) return;
        if (string.IsNullOrWhiteSpace(path)) return;
        string normalized;
        try { normalized = Path.GetFullPath(path.Trim()); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            StatusText.Text = ex.Message;
            return;
        }
        if (!Directory.Exists(normalized))
        {
            StatusText.Text = "존재하지 않거나 접근할 수 없는 폴더: " + normalized;
            return;
        }
        if (_folders.Any(existing => string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            StatusText.Text = "이미 선택한 폴더입니다.";
            return;
        }
        _folders.Add(normalized);
        StatusText.Text = $"{_folders.Count}개 폴더 선택됨";
    }

    private void Add_Click(object sender, RoutedEventArgs e) => AddFolder(FolderPathBox.Text);
    private void Path_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        AddFolder(FolderPathBox.Text);
    }
    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (_cancellation is not null) return;
        foreach (var item in FoldersBox.SelectedItems.Cast<string>().ToArray())
            _folders.Remove(item);
    }
    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (_cancellation is null) _folders.Clear();
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_cancellation is not null) return;
        if (_folders.Count == 0)
        {
            StatusText.Text = "분석할 폴더를 먼저 추가하세요.";
            return;
        }

        using var cancel = new CancellationTokenSource();
        _cancellation = cancel;
        var finished = false;
        StartButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        FolderPathBox.IsEnabled = false;
        FoldersBox.IsEnabled = false;
        _results.Clear();
        var progress = new Progress<FolderAnalysisProgress>(update =>
        {
            if (_closing || finished) return;
            if (update.CompletedRoot is { } root)
            {
                _results.Add(root);
                StatusText.Text = $"{_results.Count}개 폴더 분석 완료 · 현재: {root.Path}";
            }
            else
                StatusText.Text = $"분석 중: {update.CurrentFolder} · 파일 {update.Files:N0}개 · {FolderAnalysisItem.FormatSize(update.Bytes)}";
        });
        try
        {
            var report = await _service.AnalyzeAsync(_folders, progress, cancel.Token);
            if (_closing) return;
            finished = true;
            _results.Clear();
            foreach (var result in report.Roots)
                _results.Add(result);
            StatusText.Text = $"완료 · 파일 {report.TotalFiles:N0}개 · 하위 폴더 {report.TotalSubfolders:N0}개 · "
                + $"총용량 {FolderAnalysisItem.FormatSize(report.TotalBytes)}"
                + (report.OverlappingRootsSkipped > 0 ? $" · 중복 선택 {report.OverlappingRootsSkipped}개 제외" : "")
                + (report.SkippedFolders > 0 ? $" · 접근 불가/링크 {report.SkippedFolders}개 건너뜀" : "");
        }
        catch (OperationCanceledException)
        {
            if (!_closing) StatusText.Text = "사용자가 분석을 중지했습니다. 부분 결과만 표시됩니다.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or ArgumentException or NotSupportedException)
        {
            if (!_closing) StatusText.Text = "분석 실패: " + ex.Message;
        }
        finally
        {
            finished = true;
            if (!_closing)
            {
                StartButton.IsEnabled = true;
                CancelButton.IsEnabled = false;
                FolderPathBox.IsEnabled = true;
                FoldersBox.IsEnabled = true;
            }
            if (ReferenceEquals(_cancellation, cancel)) _cancellation = null;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cancellation?.Cancel();
}
