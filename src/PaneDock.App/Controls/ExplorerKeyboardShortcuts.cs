using System.Windows.Input;

namespace PaneDock.App.Controls;

internal enum ExplorerShortcutAction
{
    None,
    Back,
    Forward,
    Up,
    Refresh,
    EditAddress,
    Filter,
    GlobalSearch,
    NewTab,
    CloseTab,
    NextTab,
    PreviousTab,
    FocusNextArea,
    NewFolder,
    NewFile,
    Paste,
    SelectAll,
    Open,
    Rename,
    Cut,
    Copy,
    Delete,
    DeletePermanently,
    Properties,
    ContextMenu
}

/// <summary>
/// Centralizes Windows Explorer-style keyboard gestures. Text editing is
/// deliberately excluded so Ctrl+C/V, Delete and Backspace still work in
/// address and filter text boxes.
/// </summary>
internal static class ExplorerKeyboardShortcuts
{
    internal static ExplorerShortcutAction Resolve(
        Key key, ModifierKeys modifiers, bool fileListFocused, bool textEditing)
    {
        if (textEditing)
            return ExplorerShortcutAction.None;

        var action = (modifiers, key) switch
        {
            (ModifierKeys.None, Key.Back) or (ModifierKeys.Alt, Key.Left) =>
                ExplorerShortcutAction.Back,
            (ModifierKeys.Alt, Key.Right) => ExplorerShortcutAction.Forward,
            (ModifierKeys.Alt, Key.Up) => ExplorerShortcutAction.Up,
            (ModifierKeys.None, Key.F5) or (ModifierKeys.Control, Key.R) =>
                ExplorerShortcutAction.Refresh,
            (ModifierKeys.Control, Key.L) or (ModifierKeys.Alt, Key.D) or
                (ModifierKeys.None, Key.F4) => ExplorerShortcutAction.EditAddress,
            (ModifierKeys.Control, Key.F) => ExplorerShortcutAction.Filter,
            (ModifierKeys.Control, Key.E) or (ModifierKeys.None, Key.F3) =>
                ExplorerShortcutAction.GlobalSearch,
            (ModifierKeys.Control, Key.T) => ExplorerShortcutAction.NewTab,
            (ModifierKeys.Control, Key.W) => ExplorerShortcutAction.CloseTab,
            (ModifierKeys.Control, Key.Tab) or (ModifierKeys.Control, Key.Next) =>
                ExplorerShortcutAction.NextTab,
            (ModifierKeys.Control | ModifierKeys.Shift, Key.Tab) or
                (ModifierKeys.Control, Key.Prior) =>
                ExplorerShortcutAction.PreviousTab,
            (ModifierKeys.None, Key.F6) => ExplorerShortcutAction.FocusNextArea,
            (ModifierKeys.Control | ModifierKeys.Shift, Key.N) =>
                ExplorerShortcutAction.NewFolder,
            (ModifierKeys.Control | ModifierKeys.Alt, Key.N) =>
                ExplorerShortcutAction.NewFile,
            (ModifierKeys.Control, Key.V) => ExplorerShortcutAction.Paste,
            _ => ExplorerShortcutAction.None
        };
        if (action != ExplorerShortcutAction.None || !fileListFocused)
            return action;

        return (modifiers, key) switch
        {
            (ModifierKeys.Control, Key.A) => ExplorerShortcutAction.SelectAll,
            (ModifierKeys.None, Key.Return) => ExplorerShortcutAction.Open,
            (ModifierKeys.None, Key.F2) => ExplorerShortcutAction.Rename,
            (ModifierKeys.Control, Key.X) => ExplorerShortcutAction.Cut,
            (ModifierKeys.Control, Key.C) => ExplorerShortcutAction.Copy,
            (ModifierKeys.None, Key.Delete) or (ModifierKeys.Control, Key.D) =>
                ExplorerShortcutAction.Delete,
            (ModifierKeys.Shift, Key.Delete) => ExplorerShortcutAction.DeletePermanently,
            (ModifierKeys.Alt, Key.Return) => ExplorerShortcutAction.Properties,
            (ModifierKeys.Shift, Key.F10) => ExplorerShortcutAction.ContextMenu,
            _ => ExplorerShortcutAction.None
        };
    }
}
