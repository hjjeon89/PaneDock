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
    private CancellationTokenSource? _cancellation;
    private bool _closing;
    private int _searchGeneration;

    public event Action<string>? NavigateRequested;

    public SearchWindow(string root, string query)
    {
        InitializeComponent();
        RootBox.Text = root;
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
        if (query.Length == 0 || root.Length == 0)
        {
            StatusText.Text = "검색어와 폴더 경로를 입력하세요.";
            return;
        }

        var generation = ++_searchGeneration;
        var finished = false;
        using var cancel = new CancellationTokenSource();
        _cancellation = cancel;
        _results.Clear();
        SearchButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        RootBox.IsEnabled = false;
        RecursiveBox.IsEnabled = false;
        StatusText.Text = "파일 검색 중…";
        var progress = new Progress<FileSearchProgress>(batch =>
        {
            if (_closing || finished || _searchGeneration != generation) return;
            foreach (var item in batch.NewItems)
                _results.Add(item);
            StatusText.Text = $"{_results.Count:N0}개 발견 · {batch.DirectoriesScanned:N0}개 폴더 검색 중"
                + (batch.SkippedFolders > 0 ? $" · 접근 불가 {batch.SkippedFolders}개 건너뜀" : "");
        });
        try
        {
            var report = await _service.SearchAsync(root, query,
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
                StatusText.Text = $"{report.Matched:N0}개 검색됨 · {report.DirectoriesScanned:N0}개 폴더 확인"
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
                RecursiveBox.IsEnabled = true;
            }
            if (ReferenceEquals(_cancellation, cancel))
                _cancellation = null;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cancellation?.Cancel();

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
