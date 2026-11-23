using System.Windows.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PaneDock.App;
using PaneDock.App.Controls;

[TestClass]
public sealed class ExplorerKeyboardShortcutsTests
{
    [TestMethod]
    public void ExplorerNavigationAndSearchShortcutsWorkThroughoutPane()
    {
        Check(Key.Back, ModifierKeys.None, ExplorerShortcutAction.Back);
        Check(Key.Left, ModifierKeys.Alt, ExplorerShortcutAction.Back);
        Check(Key.Right, ModifierKeys.Alt, ExplorerShortcutAction.Forward);
        Check(Key.Up, ModifierKeys.Alt, ExplorerShortcutAction.Up);
        Check(Key.F5, ModifierKeys.None, ExplorerShortcutAction.Refresh);
        Check(Key.R, ModifierKeys.Control, ExplorerShortcutAction.Refresh);
        Check(Key.D, ModifierKeys.Alt, ExplorerShortcutAction.EditAddress);
        Check(Key.L, ModifierKeys.Control, ExplorerShortcutAction.EditAddress);
        Check(Key.F4, ModifierKeys.None, ExplorerShortcutAction.EditAddress);
        Check(Key.F, ModifierKeys.Control, ExplorerShortcutAction.Filter);
        Check(Key.E, ModifierKeys.Control, ExplorerShortcutAction.GlobalSearch);
        Check(Key.F3, ModifierKeys.None, ExplorerShortcutAction.GlobalSearch);
        Check(Key.F6, ModifierKeys.None, ExplorerShortcutAction.FocusNextArea);
    }

    [TestMethod]
    public void TabShortcutsAndFolderCreationWorkWithoutFileListFocus()
    {
        Check(Key.T, ModifierKeys.Control, ExplorerShortcutAction.NewTab);
        Check(Key.W, ModifierKeys.Control, ExplorerShortcutAction.CloseTab);
        Check(Key.Tab, ModifierKeys.Control, ExplorerShortcutAction.NextTab);
        Check(Key.Tab, ModifierKeys.Control | ModifierKeys.Shift,
            ExplorerShortcutAction.PreviousTab);
        Check(Key.Next, ModifierKeys.Control, ExplorerShortcutAction.NextTab);
        Check(Key.Prior, ModifierKeys.Control, ExplorerShortcutAction.PreviousTab);
        Check(Key.N, ModifierKeys.Control | ModifierKeys.Shift,
            ExplorerShortcutAction.NewFolder);
        Check(Key.N, ModifierKeys.Control | ModifierKeys.Alt,
            ExplorerShortcutAction.NewFile);
        Check(Key.V, ModifierKeys.Control, ExplorerShortcutAction.Paste);
    }

    [TestMethod]
    public void SelectedFileActionsRequireFileListFocus()
    {
        Check(Key.A, ModifierKeys.Control, ExplorerShortcutAction.SelectAll, fileFocused: true);
        Check(Key.Enter, ModifierKeys.None, ExplorerShortcutAction.Open, fileFocused: true);
        Check(Key.F2, ModifierKeys.None, ExplorerShortcutAction.Rename, fileFocused: true);
        Check(Key.C, ModifierKeys.Control, ExplorerShortcutAction.Copy, fileFocused: true);
        Check(Key.X, ModifierKeys.Control, ExplorerShortcutAction.Cut, fileFocused: true);
        Check(Key.Delete, ModifierKeys.None, ExplorerShortcutAction.Delete, fileFocused: true);
        Check(Key.D, ModifierKeys.Control, ExplorerShortcutAction.Delete, fileFocused: true);
        Check(Key.Delete, ModifierKeys.Shift,
            ExplorerShortcutAction.DeletePermanently, fileFocused: true);
        Check(Key.Enter, ModifierKeys.Alt, ExplorerShortcutAction.Properties, fileFocused: true);
        Check(Key.F10, ModifierKeys.Shift, ExplorerShortcutAction.ContextMenu, fileFocused: true);

        Check(Key.C, ModifierKeys.Control, ExplorerShortcutAction.None);
        Check(Key.Delete, ModifierKeys.Shift, ExplorerShortcutAction.None);
        Check(Key.F2, ModifierKeys.None, ExplorerShortcutAction.None);
        Check(Key.A, ModifierKeys.Control, ExplorerShortcutAction.None);
        Check(Key.Up, ModifierKeys.None, ExplorerShortcutAction.None, fileFocused: true);
        Check(Key.Space, ModifierKeys.None, ExplorerShortcutAction.None, fileFocused: true);
    }

    [TestMethod]
    public void TypingAddressOrFilterPreservesNativeTextEditing()
    {
        foreach (var modifiers in new[]
                 {
                     ModifierKeys.None, ModifierKeys.Control, ModifierKeys.Shift,
                     ModifierKeys.Alt, ModifierKeys.Control | ModifierKeys.Shift
                 })
        {
            foreach (var key in new[] { Key.Back, Key.Delete, Key.A, Key.C, Key.V, Key.X,
                         Key.Left, Key.Right, Key.Home, Key.End, Key.Enter })
                Check(key, modifiers, ExplorerShortcutAction.None,
                    fileFocused: true, editingText: true);
        }
        Check(Key.L, ModifierKeys.Control, ExplorerShortcutAction.None,
            fileFocused: true, editingText: true);
        Check(Key.F5, ModifierKeys.None, ExplorerShortcutAction.None, editingText: true);
    }

    [TestMethod]
    public void PermanentDeleteNeverUsesRecycleOrUndoFlags()
    {
        const uint recycle = 0x00080000;
        const uint addUndo = 0x20000000;
        Assert.AreNotEqual(0u, ShellFileOperation.GetOperationFlags(FileCommand.Delete) & recycle);
        Assert.AreNotEqual(0u, ShellFileOperation.GetOperationFlags(FileCommand.Delete) & addUndo);
        Assert.AreEqual(0u, ShellFileOperation.GetOperationFlags(FileCommand.DeletePermanently) & recycle);
        Assert.AreEqual(0u, ShellFileOperation.GetOperationFlags(FileCommand.DeletePermanently) & addUndo);
        Assert.AreNotEqual(0u, ShellFileOperation.GetOperationFlags(FileCommand.DeletePermanently));
    }

    [TestMethod]
    public void HelpIncludesEverySupportedExplorerCommand()
    {
        var entries = ShortcutHelpCatalog.Groups.SelectMany(group => group.Entries).ToArray();
        Assert.AreEqual(Enum.GetValues<ExplorerShortcutAction>().Length - 1,
            entries.Count(entry => entry.Action is not null),
            "Each explorer action should have one documented shortcut.");
        foreach (var action in Enum.GetValues<ExplorerShortcutAction>()
                     .Where(action => action != ExplorerShortcutAction.None))
        {
            Assert.AreEqual(1, entries.Count(entry => entry.Action == action),
                $"Missing or duplicated help entry: {action}");
        }
        Assert.IsTrue(entries.Any(entry => entry.Keys == "F1"),
            "The help opening gesture should be shown.");
    }

    private static void Check(Key key, ModifierKeys modifiers, ExplorerShortcutAction expected,
        bool fileFocused = false, bool editingText = false) =>
        Assert.AreEqual(expected,
            ExplorerKeyboardShortcuts.Resolve(key, modifiers, fileFocused, editingText),
            $"{modifiers}+{key}, file focus={fileFocused}, editing text={editingText}");
}
