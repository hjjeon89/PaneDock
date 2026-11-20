using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PaneDock.Core.Models;
using PaneDock.Core.Services;

namespace PaneDock.App;

public partial class SearchWindow : Window
{
    private readonly FileSearchService _service = new();
    private readonly EverythingIndexedSearchService _indexed = new();
    private readonly ObservableCollection<FileSearchResult> _results = [];
    private readonly string _currentFolder;
    private readonly DispatcherTimer _typingDelay = new()
    {
        Interval = TimeSpan.FromMilliseconds(230)
    };
    private CancellationTokenSource? _cancellation;
    private bool _indexReady;
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
        _typingDelay.Tick += async (_, _) =>
        {
            _typingDelay.Stop();
            await StartSearchAsync(automatic: true);
        };
        Closing += (_, _) =>
        {
            _closing = true;
            _typingDelay.Stop();
            _searchGeneration++;
            _cancellation?.Cancel();
        };
        Loaded += async (_, _) =>
        {
            QueryBox.Focus();
            StatusText.Text = "Everything 인덱스 연결 확인 중…";
            if (_indexed.IsAvailable)
            {
                try
                {
                    // Probe IPC, not just the process name: Everything Lite and
                    // inaccessible/initializing instances cannot answer queries.
                    _indexReady = await _indexed.TrySearchAsync("PaneDock", null, true,
                        1, CancellationToken.None) is not null;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                           or ArgumentException)
                {
                    _indexReady = false;
                }
            }
            if (_closing) return;
            StatusText.Text = _indexReady
                ? "Everything 인덱스 연결됨 · 검색어 입력 시 자동 검색 (약 230ms 대기)"
                : "Everything 연결 불가 · 검색 버튼을 누르면 직접 검색합니다.";
            if (!string.IsNullOrWhiteSpace(QueryBox.Text))
                await StartSearchAsync();
        };
    }

    private async void Search_Click(object sender, RoutedEventArgs e) =>
        await StartSearchAsync();

    private void SearchInputs_Changed(object sender, TextChangedEventArgs e) =>
        QueueLiveSearch();

    private void SearchScope_Changed(object sender, RoutedEventArgs e) =>
        QueueLiveSearch();

    private void QueueLiveSearch()
    {
        if (!IsLoaded || _closing) return;
        _typingDelay.Stop();
        _cancellation?.Cancel();
        _searchGeneration++;
        if (string.IsNullOrWhiteSpace(QueryBox.Text))
        {
            ResultGrid.ItemsSource = _results;
            _results.Clear();
            StatusText.Text = "찾을 파일명이나 폴더명 검색어를 입력하세요.";
            return;
        }

        if (_indexReady && DirectScanBox.IsChecked != true)
        {
            StatusText.Text = "Everything 인덱스 검색 준비 중…";
            _typingDelay.Start();
        }
        else
            StatusText.Text = "직접 파일 검색은 검색 버튼 또는 Enter로 시작하세요.";
    }

    private async void Query_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await StartSearchAsync();
    }

    private async Task StartSearchAsync(bool automatic = false)
    {
        if (_closing) return;
        _typingDelay.Stop();
        var query = QueryBox.Text.Trim();
        var root = RootBox.Text.Trim();
        if (query.Length == 0)
        {
            StatusText.Text = "찾을 파일명이나 폴더명 검색어를 입력하세요.";
            return;
        }

        var searchAll = root.Length == 0 || string.Equals(root, "내 PC", StringComparison.OrdinalIgnoreCase)
            || ExplorerLocations.IsThisPc(root);
        var usingIndex = _indexReady && DirectScanBox.IsChecked != true;
        if (automatic && !usingIndex) return;
        _cancellation?.Cancel();
        var generation = ++_searchGeneration;
        var finished = false;
        var cancel = new CancellationTokenSource();
        _cancellation = cancel;
        var clock = Stopwatch.StartNew();
        _results.Clear();
        ResultGrid.ItemsSource = _results;
        SearchButton.IsEnabled = usingIndex;
        CancelButton.IsEnabled = true;
        RootBox.IsEnabled = usingIndex;
        CurrentFolderButton.IsEnabled = usingIndex && !ExplorerLocations.IsThisPc(_currentFolder);
        RecursiveBox.IsEnabled = usingIndex;
        DirectScanBox.IsEnabled = usingIndex;
        StatusText.Text = usingIndex ? "Everything 인덱스에서 검색 중…"
            : searchAll ? "PC 전체 드라이브 직접 검색 중… (인덱스 미사용)"
            : "폴더 직접 검색 중…";
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
            var report = usingIndex
                ? await _indexed.TrySearchAsync(query, searchAll ? null : root,
                    RecursiveBox.IsChecked == true, 5000, cancel.Token)
                : null;
            var wasIndexed = report is not null;
            if (report is null)
            {
                if (automatic)
                {
                    if (!_closing && generation == _searchGeneration)
                    {
                        _indexReady = false;
                        StatusText.Text = "Everything 연결이 끊겼습니다. 검색 버튼으로 직접 검색하세요.";
                    }
                    return;
                }
                if (usingIndex)
                    _indexReady = false;
                var roots = searchAll ? FileSearchService.GetReadyDriveRoots() : [root];
                report = await _service.SearchAsync(roots, query,
                    RecursiveBox.IsChecked == true, progress, cancel.Token);
            }
            if (!_closing && _searchGeneration == generation)
            {
                finished = true;
                // Swap once rather than thousands of individual WPF notifications.
                ResultGrid.ItemsSource = report.Results;
                clock.Stop();
                if (wasIndexed)
                    StatusText.Text = $"Everything 인덱스 · {report.Matched:N0}개 표시"
                        + (report.TotalMatches is { } total
                            ? $" / 전체 {total:N0}개" : "")
                        + $" · {clock.ElapsedMilliseconds:N0}ms"
                        + (report.LimitReached ? " · 처음 5,000개만 표시" : "");
                else
                    StatusText.Text = $"{(searchAll ? "PC 전체" : "폴더")} 직접 검색 완료 · "
                        + $"{report.Matched:N0}개 · {report.DirectoriesScanned:N0}개 폴더"
                        + $" · {clock.ElapsedMilliseconds:N0}ms"
                        + (report.SkippedFolders > 0 ? $" · 건너뜀 {report.SkippedFolders:N0}개" : "")
                        + (report.LimitReached ? " · 최대 5,000개 제한" : "");
            }
        }
        catch (OperationCanceledException)
        {
            if (!_closing && generation == _searchGeneration)
                StatusText.Text = $"검색 취소 · {_results.Count:N0}개 결과";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or ArgumentException or NotSupportedException)
        {
            if (!_closing && generation == _searchGeneration)
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
                DirectScanBox.IsEnabled = true;
            }
            if (ReferenceEquals(_cancellation, cancel))
                _cancellation = null;
            cancel.Dispose();
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
