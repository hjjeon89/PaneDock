using System.Collections;
using System.Windows.Controls;
using System.Windows.Input;
using PaneDock.Core.Models;

namespace PaneDock.App.Controls;

// Incremental name navigation is local to the visible, sorted/filtered list.
// It never enumerates the file system or modifies the filter.
internal sealed class ExplorerNameNavigation
{
    internal const long ResetAfterMilliseconds = 1000;
    private string _prefix = "";
    private long _lastInput;

    internal static bool TryGetLetter(Key key, ModifierKeys modifiers,
        bool listFocused, bool editingText, out char letter)
    {
        letter = default;
        if (!listFocused || editingText ||
            modifiers is not (ModifierKeys.None or ModifierKeys.Shift) ||
            key < Key.A || key > Key.Z)
            return false;

        letter = (char)('A' + (int)key - (int)Key.A);
        return true;
    }

    internal void Reset() => _prefix = "";

    internal void SelectNext(DataGrid files, char letter, long now)
    {
        var index = FindNext(files.Items, files.SelectedIndex, letter, now);
        if (index < 0 || files.Items[index] is not ExplorerEntry match)
            return;

        if (files.SelectedItems.Count > 1)
            files.UnselectAll();
        files.SelectedItem = match;
        files.CurrentCell = new DataGridCellInfo(match, files.Columns[0]);
        files.ScrollIntoView(match);
    }

    internal int FindNext(IList items, int selectedIndex, char letter, long now)
    {
        letter = char.ToUpperInvariant(letter);
        var continuing = _prefix.Length > 0 && now - _lastInput < ResetAfterMilliseconds;
        var previous = continuing ? _prefix : "";
        _prefix = previous + letter;
        _lastInput = now;

        if (items.Count == 0)
        {
            Reset();
            return -1;
        }

        // The first letter moves past the current selection and wraps.
        // Additional letters refine the prefix, including the selected row.
        var start = selectedIndex < 0 || selectedIndex >= items.Count
            ? 0 : continuing ? selectedIndex : (selectedIndex + 1) % items.Count;
        var match = Find(items, start, _prefix);
        // If repeating a letter cannot refine the prefix (e.g. no "BB"),
        // cycle through the names starting with that letter.
        if (match < 0 && previous.Length == 1 && previous[0] == letter)
        {
            _prefix = previous;
            var next = selectedIndex >= 0 && selectedIndex < items.Count
                ? (selectedIndex + 1) % items.Count : 0;
            match = Find(items, next, _prefix);
        }
        return match;
    }

    private static int Find(IList items, int start, string prefix)
    {
        for (var offset = 0; offset < items.Count; offset++)
        {
            var index = (start + offset) % items.Count;
            if (items[index] is ExplorerEntry entry &&
                entry.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return index;
        }
        return -1;
    }
}
