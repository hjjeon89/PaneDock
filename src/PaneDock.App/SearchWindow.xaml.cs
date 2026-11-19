using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PaneDock.Core.Models;
using PaneDock.Core.Services;

namespace PaneDock.App;

public partial class SearchWindow : Window
{
    private readonly FileSearchService _service = new();
    private readonly ObservableCollection<FileSearchResult> _results = [];
    private readonly string _currentFolder;
    private CancellationTokenSource? _cancellation;
    private bool _closing;
    private int _searchGeneration;

    public event Action<string>? NavigateRequested;

    public SearchWindow(string root, string query)
    {
        InitializeComponent();
        _currentFolder = root;
        // Empty scope means all ready drives (Everything-like scope, without indexing).
        RootBox.Text = "";
        CurrentFolderButton.IsEnabled = !ExplorerLocations.IsThisPc(root)
            && !string.IsNullOrWhiteSpace(root);
        QueryBox.Text = query;
        ResultGrid.ItemsSource = _results;
        Closing += (_, _) =>
        {
            _closing = true;
            _searchGeneration++;
            _cancellation?.Cancel();
        };
        Loaded += (_, _) =>
        {
            QueryBox.Focus();
            if (!string.IsNullOrWhiteSpace(QueryBox.Text))
                _ = StartSearchAsync();
        };
    }

    private async void Search_Click(object sender, RoutedEventArgs e) =>
        await StartSearchAsync();

    private async void Query_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await StartSearchAsync();
    }

    private async Task StartSearchAsync()
    {
        if (_cancellation is not null)
            return;
        var query = QueryBox.Text.Trim();
        var root = RootBox.Text.Trim();
        if (query.Length == 0)
        {
            StatusText.Text = "찾을 파일명이나 폴더명 검색어를 입력하세요.";
            return;
        }

        var searchAll = root.Length == 0 || string.Equals(root, "내 PC", StringComparison.OrdinalIgnoreCase)
            || ExplorerLocations.IsThisPc(root);
        var generation = ++_searchGeneration;
        var finished = false;
        using var cancel = new CancellationTokenSource();
        _cancellation = cancel;
        _results.Clear();
        SearchButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        RootBox.IsEnabled = false;
        CurrentFolderButton.IsEnabled = false;
        RecursiveBox.IsEnabled = false;
        StatusText.Text = searchAll ? "PC 전체 드라이브 검색 중… (인덱스 없이 순차 검색)" : "폴더 검색 중…";
        var progress = new Progress<FileSearchProgress>(batch =>
        {
            if (_closing || finished || _searchGeneration != generation) return;
            foreach (var item in batch.NewItems)
                _results.Add(item);
            StatusText.Text = $"{_results.Count:N0}개 발견 · {batch.DirectoriesScanned:N0}개 폴더 검색 중"
                + (searchAll ? $" · {Path.GetPathRoot(batch.CurrentFolder ?? "")}" : "")
                + (batch.SkippedFolders > 0 ? $" · 접근 불가 {batch.SkippedFolders}개 건너뜀" : "");
        });
        try
        {
            var roots = searchAll ? FileSearchService.GetReadyDriveRoots() : [root];
            var report = await _service.SearchAsync(roots, query,
                RecursiveBox.IsChecked == true, progress, cancel.Token);
            if (!_closing && _searchGeneration == generation)
            {
                finished = true;
                // Progress<T> callbacks are posted asynchronously. Reconcile
                // from the authoritative result so late callbacks cannot leave
                // a partial list or overwrite the completion status.
                _results.Clear();
                foreach (var item in report.Results)
                    _results.Add(item);
                StatusText.Text = $"{(searchAll ? "PC 전체 검색 완료" : "폴더 검색 완료")} · "
                    + $"{report.Matched:N0}개 검색됨 · {report.DirectoriesScanned:N0}개 폴더 확인"
                    + (report.SkippedFolders > 0 ? $" · 접근 불가 {report.SkippedFolders:N0}개" : "")
                    + (report.LimitReached ? " · 최대 5,000개 결과 제한" : "");
            }
        }
        catch (OperationCanceledException)
        {
            if (!_closing) StatusText.Text = $"검색 취소 · {_results.Count:N0}개 결과";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or ArgumentException or NotSupportedException)
        {
            if (!_closing)
                StatusText.Text = "검색 실패: " + ex.Message;
        }
        finally
        {
            finished = true;
            if (_searchGeneration == generation && !_closing)
            {
                SearchButton.IsEnabled = true;
                CancelButton.IsEnabled = false;
                RootBox.IsEnabled = true;
                CurrentFolderButton.IsEnabled = !ExplorerLocations.IsThisPc(_currentFolder)
                    && !string.IsNullOrWhiteSpace(_currentFolder);
                RecursiveBox.IsEnabled = true;
            }
            if (ReferenceEquals(_cancellation, cancel))
                _cancellation = null;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cancellation?.Cancel();

    private void CurrentFolder_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentFolderButton.IsEnabled)
            RootBox.Text = _currentFolder;
    }

    private void Results_DoubleClick(object sender, MouseButtonEventArgs e) => OpenSelected();
    private void OpenResult_Click(object sender, RoutedEventArgs e) => OpenSelected();

    private void OpenSelected()
    {
        if (ResultGrid.SelectedItem is not FileSearchResult item) return;
        try
        {
            Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or IOException
                                   or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(this, ex.Message, "파일 열기 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenLocation_Click(object sender, RoutedEventArgs e)
    {
        if (ResultGrid.SelectedItem is FileSearchResult item)
            NavigateRequested?.Invoke(item.FolderPath);
    }
}
