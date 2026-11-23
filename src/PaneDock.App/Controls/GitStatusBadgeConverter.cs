using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using PaneDock.Core.Models;

namespace PaneDock.App.Controls;

public sealed record GitStatusBadge(string Symbol, Brush Color, string Description);

/// <summary>
/// Tiny overlay on the existing Windows file icon; does not replace icons
/// or alter selected ExplorerEntry objects.
/// </summary>
public sealed class GitStatusBadgeConverter : IMultiValueConverter
{
    private static readonly Brush CleanBrush = FrozenBrush(0x28, 0xA0, 0x69);
    private static readonly Brush ChangedBrush = FrozenBrush(0xD2, 0x84, 0x1E);
    private static readonly Brush StagedBrush = FrozenBrush(0x2B, 0x79, 0xC2);
    private static readonly Brush ConflictBrush = FrozenBrush(0xD7, 0x3F, 0x4C);
    private static readonly Brush UntrackedBrush = FrozenBrush(0x6D, 0x8B, 0xA6);

    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length != 3 || values[0] is not string path
            || values[1] is not bool isFolder
            || values[2] is not GitRepositorySnapshot snapshot
            || snapshot.GetStatus(path, isFolder) is not { } status)
            return null;

        var detail = isFolder ? "하위 항목" : "파일";
        return status.State switch
        {
            GitFileState.Clean => new GitStatusBadge("✓", CleanBrush,
                "Git: 변경 없음 · HEAD와 동일한 상태"),
            GitFileState.Modified => new GitStatusBadge("●", ChangedBrush,
                $"Git: {detail} 수정 중" + (status.AlsoStaged
                    ? " · 스테이징 후 추가 변경 포함" : "")),
            GitFileState.Staged => new GitStatusBadge("+", StagedBrush,
                $"Git: {detail} 스테이징됨 · 커밋 대기"),
            GitFileState.Untracked => new GitStatusBadge("?", UntrackedBrush,
                $"Git: {detail} 추적되지 않음"),
            GitFileState.Conflict => new GitStatusBadge("!", ConflictBrush,
                $"Git: {detail} 병합 충돌 · 해결 필요"),
            _ => null
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Brush FrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
