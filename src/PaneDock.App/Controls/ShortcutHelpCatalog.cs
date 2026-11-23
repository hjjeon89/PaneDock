namespace PaneDock.App.Controls;

internal sealed record ShortcutHelpEntry(string Keys, string Description,
    ExplorerShortcutAction? Action = null);

internal sealed record ShortcutHelpGroup(string Title, IReadOnlyList<ShortcutHelpEntry> Entries);

/// <summary>
/// Shortcuts shown by the in-app help panel. Keep action labels aligned with
/// ExplorerKeyboardShortcuts.Resolve; the corresponding regression test checks
/// that every supported explorer action has a help entry.
/// </summary>
internal static class ShortcutHelpCatalog
{
    internal static IReadOnlyList<ShortcutHelpGroup> Groups { get; } =
    [
        new("탐색 · 검색",
        [
            new("Backspace / Alt+←", "이전 방문 폴더", ExplorerShortcutAction.Back),
            new("Alt+→", "앞으로 이동", ExplorerShortcutAction.Forward),
            new("Alt+↑", "상위 폴더", ExplorerShortcutAction.Up),
            new("F5 / Ctrl+R", "새로 고침", ExplorerShortcutAction.Refresh),
            new("Ctrl+L / Alt+D / F4", "주소 입력", ExplorerShortcutAction.EditAddress),
            new("Ctrl+F", "현재 폴더 필터", ExplorerShortcutAction.Filter),
            new("Ctrl+E / F3", "전체 파일 검색", ExplorerShortcutAction.GlobalSearch),
            new("F6", "파일 목록·폴더 트리 포커스", ExplorerShortcutAction.FocusNextArea)
        ]),
        new("파일 · 폴더",
        [
            new("Enter", "선택 파일 열기 / 폴더 진입", ExplorerShortcutAction.Open),
            new("Ctrl+A", "전체 선택", ExplorerShortcutAction.SelectAll),
            new("Ctrl+C", "복사", ExplorerShortcutAction.Copy),
            new("Ctrl+X", "잘라내기", ExplorerShortcutAction.Cut),
            new("Ctrl+V", "붙여넣기", ExplorerShortcutAction.Paste),
            new("F2", "이름 변경", ExplorerShortcutAction.Rename),
            new("Delete / Ctrl+D", "휴지통으로 삭제", ExplorerShortcutAction.Delete),
            new("Shift+Delete", "확인 후 영구 삭제", ExplorerShortcutAction.DeletePermanently),
            new("Ctrl+Shift+N", "새 폴더", ExplorerShortcutAction.NewFolder),
            new("Ctrl+Alt+N", "새 파일", ExplorerShortcutAction.NewFile),
            new("Alt+Enter", "선택 항목 속성", ExplorerShortcutAction.Properties),
            new("Shift+F10", "파일 메뉴 열기", ExplorerShortcutAction.ContextMenu)
        ]),
        new("탭 관리",
        [
            new("Ctrl+T", "새 탭", ExplorerShortcutAction.NewTab),
            new("Ctrl+W", "현재 탭 닫기", ExplorerShortcutAction.CloseTab),
            new("Ctrl+Tab / Ctrl+PageDown", "다음 탭", ExplorerShortcutAction.NextTab),
            new("Ctrl+Shift+Tab / Ctrl+PageUp", "이전 탭", ExplorerShortcutAction.PreviousTab)
        ]),
        new("도움말",
        [
            new("F1", "단축키 도움말 열기 / 닫기")
        ])
    ];
}
