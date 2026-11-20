using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using PaneDock.Core.Models;
using PaneDock.Core.Services;

namespace PaneDock.App;

/// <summary>
/// Embedded search surface hosted by the main window's title-bar search Popup.
/// Its search state survives opening/closing the popup.
/// </summary>
public partial class SearchPanel : UserControl
{
    private readonly FileSearchService _service = new();
    private readonly EverythingIndexedSearchService _indexed = new();
    private readonly ObservableCollection<FileSearchResult> _results = [];
    private string _currentFolder = ExplorerLocations.ThisPc;
    private readonly DispatcherTimer _typingDelay = new()
    {
        Interval = TimeSpan.FromMilliseconds(230)
    };
    private CancellationTokenSource? _cancellation;
    private bool _indexReady;
    private bool _closing;
    private bool _active;
    private bool _deletionRunning;
    private int _searchGeneration;
    private int _activationGeneration;

    public event Action<string>? NavigateRequested;
    public event Action<string[]>? FileLocationsChanged;
    public event Action<bool>? DeletionBusyChanged;
    public event Action? CloseRequested;

    public Window? OwnerWindow { get; set; }
    public bool IsDeletionRunning => _deletionRunning;

    public SearchPanel()
    {
        InitializeComponent();
        RootBox.Text = "";
        CurrentFolderButton.IsEnabled = false;
        ResultGrid.ItemsSource = _results;
        _typingDelay.Tick += async (_, _) =>
        {
            _typingDelay.Stop();
            await StartSearchAsync(automatic: true);
        };
    }

    public void PrepareOpen(string currentFolder, string query)
    {
        _currentFolder = currentFolder;
        // Starting from the title bar defaults to a full-PC indexed search.
        RootBox.Text = "";
        DirectScanBox.IsChecked = false;
        RecursiveBox.IsChecked = true;
        CurrentFolderButton.IsEnabled = !ExplorerLocations.IsThisPc(currentFolder)
            && !string.IsNullOrWhiteSpace(currentFolder);
        SetQuery(query);
    }

    public void SetQuery(string query) => QueryBox.Text = query;

    public Task SearchNowAsync() => StartSearchAsync();

    public async Task ActivateAsync()
    {
        if (_closing) return;
        _active = true;
        var activation = ++_activationGeneration;
        // Keep keyboard focus in the visible title-bar search field.
        StatusText.Text = "Everything 인덱스 연결 확인 중…";
        _indexReady = false;
        if (_indexed.IsAvailable)
        {
            try
            {
                _indexReady = await _indexed.TrySearchAsync("PaneDock", null, true,
                    1, CancellationToken.None) is not null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException)
            {
                _indexReady = false;
            }
        }
        if (_closing || !_active || activation != _activationGeneration) return;
        StatusText.Text = _indexReady
            ? "Everything 인덱스 연결됨 · 입력 후 약 230ms에 자동 검색"
            : "Everything 연결 불가 · 검색 버튼으로 직접 검색하세요.";
        // Direct filesystem scans must remain explicitly user-initiated:
        // opening a popup must never silently traverse the whole C: drive.
        if (!string.IsNullOrWhiteSpace(QueryBox.Text)
            && _indexReady && DirectScanBox.IsChecked != true)
            await StartSearchAsync(automatic: true);
        else if (!string.IsNullOrWhiteSpace(QueryBox.Text))
            StatusText.Text = "직접 파일 검색은 검색 버튼 또는 Enter로 시작하세요.";
    }

    public void Deactivate()
    {
        if (_deletionRunning) return;
        _active = false;
        _activationGeneration++;
        _typingDelay.Stop();
        _searchGeneration++;
        _cancellation?.Cancel();
    }

    public void Shutdown()
    {
        _closing = true;
        Deactivate();
        _typingDelay.Stop();
        _searchGeneration++;
        _cancellation?.Cancel();
    }

    private void CloseSearch_Click(object sender, RoutedEventArgs e)
    {
        if (!_deletionRunning)
            CloseRequested?.Invoke();
    }

    private void SearchPanel_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !_deletionRunning)
        {
            e.Handled = true;
            CloseRequested?.Invoke();
        }
    }

    private async void Search_Click(object sender, RoutedEventArgs e) =>
        await StartSearchAsync();

    private void SearchInputs_Changed(object sender, TextChangedEventArgs e) =>
        QueueLiveSearch();

    private void SearchScope_Changed(object sender, RoutedEventArgs e) =>
        QueueLiveSearch();

    private void QueueLiveSearch()
    {
        if (!IsLoaded || !_active || _closing || _deletionRunning) return;
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
        if (_closing || !_active || _deletionRunning) return;
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
        UpdateSelectionState();
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
                ? await _indexed.TrySearchForUiAsync(query, searchAll ? null : root,
                    RecursiveBox.IsChecked == true, 5000, cancel.Token)
                : null;
            // A superseded live query must not trigger the much slower direct
            // filesystem fallback when the indexed search returned null.
            if (cancel.IsCancellationRequested)
            {
                if (!_closing && generation == _searchGeneration)
                    StatusText.Text = $"검색 취소 · {_results.Count:N0}개 결과";
                return;
            }
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
                UpdateSelectionState();
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
            if (!_closing)
                UpdateSelectionState();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cancellation?.Cancel();

    private void CurrentFolder_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentFolderButton.IsEnabled)
            RootBox.Text = _currentFolder;
    }

    private void Results_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateSelectionState();

    private void UpdateSelectionState()
    {
        if (SelectionText is null || DeleteResultsButton is null)
            return;

        var selected = ResultGrid.SelectedItems.OfType<FileSearchResult>().ToArray();
        var folders = selected.Count(item => item.IsDirectory);
        SelectionText.Text = selected.Length == 0
            ? "선택 0개 · Ctrl/Shift 다중 선택 · Ctrl+A 전체 선택 · Delete 휴지통"
            : $"선택 {selected.Length:N0}개 (파일 {selected.Length - folders:N0}, 폴더 {folders:N0})"
              + " · Delete: 휴지통 이동";
        DeleteResultsButton.IsEnabled = selected.Length > 0
            && !_deletionRunning && _cancellation is null;
    }

    private async void Results_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            ResultGrid.SelectAll();
        }
        else if (e.Key == Key.Delete)
        {
            e.Handled = true;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                StatusText.Text = "검색창에서는 영구 삭제를 지원하지 않습니다. Delete 키로 휴지통 이동을 사용하세요.";
                return;
            }
            await DeleteSelectedAsync();
        }
    }

    private async void DeleteSelected_Click(object sender, RoutedEventArgs e) =>
        await DeleteSelectedAsync();

    private async Task DeleteSelectedAsync()
    {
        if (_closing || _deletionRunning || _cancellation is not null)
            return;

        var targets = SearchResultSelection.DeletionTargets(
            ResultGrid.SelectedItems.OfType<FileSearchResult>());
        if (targets.Count == 0)
        {
            StatusText.Text = "삭제할 파일 또는 폴더를 선택하세요.";
            return;
        }

        // Indexed results can be stale. Do not queue paths that no longer exist.
        var existing = targets.Where(item =>
            File.Exists(item.FullPath) || Directory.Exists(item.FullPath)).ToArray();
        if (existing.Length == 0)
        {
            ResultGrid.ItemsSource = SearchResultSelection.WithoutDeleted(
                CurrentResults(), targets);
            StatusText.Text = "선택 항목이 이미 삭제되어 결과 목록에서 제외했습니다.";
            UpdateSelectionState();
            return;
        }

        var directoryCount = existing.Count(item => item.IsDirectory);
        var message = $"{existing.Length:N0}개 항목을 휴지통으로 이동하시겠습니까?\n"
            + $"파일 {existing.Length - directoryCount:N0}개 / 폴더 {directoryCount:N0}개\n\n"
            + (directoryCount > 0 ? "폴더를 삭제하면 그 안에 있는 파일과 하위 폴더도 함께 삭제됩니다.\n\n" : "")
            + "휴지통을 지원하지 않는 위치에서는 Windows에서 영구 삭제 경고가 표시될 수 있습니다.";
        var owner = OwnerWindow ?? Window.GetWindow(this) ?? Application.Current.MainWindow;
        if (owner is null)
            return;

        // Popup must stay open while a modal confirmation or Shell delete
        // dialog is active, otherwise the mouse capture can dismiss it.
        _deletionRunning = true;
        DeletionBusyChanged?.Invoke(true);
        if (MessageBox.Show(owner, message, "검색 결과 삭제 확인",
                MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            _deletionRunning = false;
            DeletionBusyChanged?.Invoke(false);
            return;
        }

        _typingDelay.Stop();
        _searchGeneration++;
        SetDeleteBusy(true);
        StatusText.Text = $"{existing.Length:N0}개 항목을 휴지통으로 이동 중…";
        bool completed = false;
        Exception? failure = null;
        try
        {
            completed = await ShellFileOperation.RunAsync(
                new WindowInteropHelper(owner).Handle, FileCommand.Delete, string.Empty,
                existing.Select(item => item.FullPath).ToArray());
        }
        catch (Exception ex) when (ex is ExternalException or IOException
                                   or ArgumentException or UnauthorizedAccessException
                                   or NotSupportedException)
        {
            failure = ex;
        }
        finally
        {
            // Shell operations may be canceled midway. Trust actual filesystem
            // presence, not just the COM operation's completed/aborted flag.
            var gone = existing.Where(item =>
                !File.Exists(item.FullPath) && !Directory.Exists(item.FullPath)).ToArray();
            var paneRefreshFailed = false;
            if (gone.Length > 0)
            {
                ResultGrid.ItemsSource = SearchResultSelection.WithoutDeleted(
                    CurrentResults(), gone);
                var folders = gone.Select(item => item.FolderPath)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                try
                {
                    FileLocationsChanged?.Invoke(folders);
                }
                catch (Exception ex)
                {
                    // The delete already happened. A pane refresh failure must
                    // never leave this search window permanently locked.
                    Trace.WriteLine("검색 삭제 후 탐색 패널 새로 고침 실패: " + ex);
                    paneRefreshFailed = true;
                }
            }

            _deletionRunning = false;
            DeletionBusyChanged?.Invoke(false);
            SetDeleteBusy(false);
            UpdateSelectionState();

            var remaining = existing.Length - gone.Length;
            StatusText.Text = gone.Length == existing.Length && completed
                ? $"{gone.Length:N0}개 항목을 휴지통으로 이동했습니다."
                : $"삭제 완료 {gone.Length:N0}개 / 미처리 {remaining:N0}개"
                  + (failure is not null ? " · 오류: " + failure.Message
                    : completed ? "" : " · 작업 취소 또는 일부 실패");
            if (paneRefreshFailed)
                StatusText.Text += " · 탐색 패널을 F5로 새로 고침하세요.";
        }
    }

    private IEnumerable<FileSearchResult> CurrentResults() =>
        ResultGrid.ItemsSource is IEnumerable<FileSearchResult> items
            ? items : [];

    private void SetDeleteBusy(bool busy)
    {
        ResultGrid.IsEnabled = !busy;
        QueryBox.IsEnabled = !busy;
        RootBox.IsEnabled = !busy;
        RecursiveBox.IsEnabled = !busy;
        DirectScanBox.IsEnabled = !busy;
        CurrentFolderButton.IsEnabled = !busy && !ExplorerLocations.IsThisPc(_currentFolder)
            && !string.IsNullOrWhiteSpace(_currentFolder);
        SearchButton.IsEnabled = !busy;
        DeleteResultsButton.IsEnabled = !busy && ResultGrid.SelectedItems.Count > 0;
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
            MessageBox.Show(OwnerWindow ?? Application.Current.MainWindow,
                ex.Message, "파일 열기 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenLocation_Click(object sender, RoutedEventArgs e)
    {
        if (ResultGrid.SelectedItem is FileSearchResult item)
            NavigateRequested?.Invoke(item.FolderPath);
    }
}
