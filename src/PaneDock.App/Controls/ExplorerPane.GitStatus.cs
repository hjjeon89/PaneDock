using System.Windows;
using System.Windows.Threading;
using PaneDock.Core.Models;
using PaneDock.Core.Services;

namespace PaneDock.App.Controls;

public partial class ExplorerPane
{
    private static readonly GitStatusService GitStatuses = new();
    private readonly DispatcherTimer _gitStatusTimer = new()
    {
        Interval = TimeSpan.FromSeconds(5)
    };
    private int _gitRequestVersion;

    public static readonly DependencyProperty GitSnapshotProperty =
        DependencyProperty.Register(nameof(GitSnapshot), typeof(GitRepositorySnapshot),
            typeof(ExplorerPane));

    public GitRepositorySnapshot? GitSnapshot
    {
        get => (GitRepositorySnapshot?)GetValue(GitSnapshotProperty);
        private set => SetValue(GitSnapshotProperty, value);
    }

    private async Task RefreshGitStatusAsync(bool force = false)
    {
        var current = SelectedTab;
        var path = current?.Path;
        var version = ++_gitRequestVersion;
        if (_disposed || current is null || string.IsNullOrWhiteSpace(path)
            || ExplorerLocations.IsThisPc(path))
        {
            DisplayGitStatus(null);
            return;
        }

        // Repository lookup + Git CLI queries run outside the UI thread.
        var snapshot = await GitStatuses.GetSnapshotAsync(path, force);
        if (_disposed || version != _gitRequestVersion
            || !ReferenceEquals(SelectedTab, current)
            || !SamePath(path, current.Path))
            return; // The user navigated to another folder or tab.

        DisplayGitStatus(snapshot);
    }

    private void DisplayGitStatus(GitRepositorySnapshot? snapshot)
    {
        if (!ReferenceEquals(GitSnapshot, snapshot))
            GitSnapshot = snapshot;

        GitBranchBadge.Visibility = snapshot is null
            ? Visibility.Collapsed : Visibility.Visible;
        GitBranchBadge.Text = snapshot is null ? "" : "⑂ " + snapshot.Branch;
        GitBranchBadge.ToolTip = snapshot is null ? null
            : $"Git 저장소: {snapshot.Root}\n브랜치: {snapshot.Branch}"
              + "\n\n✓ 변경 없음 · ● 수정 중 · + 스테이징"
              + "\n? 미추적 · ! 병합 충돌";
    }
}
